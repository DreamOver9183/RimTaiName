using System;
using System.Threading;
using System.Threading.Tasks;
using RimTaiName.Llm;

namespace RimTaiName.Pipeline
{
    /// <summary>
    /// The translation pipeline (v3 §6). <see cref="ResolveLocal"/> answers everything it can without the network,
    /// field by field: locked / blank / already Chinese → special form → nickname alias → personal cache →
    /// built-in dictionary; the rest is marked <see cref="FieldSource.Pending"/>. <see cref="ResolveRemoteAsync"/>
    /// sends the pending fields to Gemini in one request. Pure C#: safe to run on a worker thread.
    /// </summary>
    public sealed class NameProcessorService
    {
        private readonly BuiltInDictionary dictionary;

        private readonly PersonalNameCache cache;

        public NameProcessorService(BuiltInDictionary dictionary, PersonalNameCache cache)
        {
            this.dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
            this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        public NameResult ResolveLocal(NameRequestPayload payload)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));
            if (payload.Mode != ProcessMode.Translate)
                throw new NotSupportedException("Only ProcessMode.Translate is implemented (Generate arrives in Step 6).");
            FieldResult first = ResolveField(payload, NameField.First);
            FieldResult last = ResolveField(payload, NameField.Last);
            FieldResult nick = ResolveNick(payload, first, last);
            return new NameResult(first, nick, last);
        }

        /// <summary>
        /// Translates the pending fields of <paramref name="local"/> with one call. Successful, sanitized results
        /// are added to the personal cache (origin api); locked and already resolved fields are never touched.
        /// </summary>
        public async Task<NameResult> ResolveRemoteAsync(NameRequestPayload payload, NameResult local, ILlmClient client,
                                                         CancellationToken cancellationToken = default(CancellationToken))
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));
            if (local == null)
                throw new ArgumentNullException(nameof(local));
            if (client == null)
                throw new ArgumentNullException(nameof(client));

            // Describe the nickname as resolved for this request, not as it was when the dialog opened:
            // a nickname the player retyped is translated on its own even if it used to be an alias.
            NameField? aliasOf = local[NameField.Nick].AliasOf;
            var request = new LlmRequest
            {
                NickSource = aliasOf == NameField.First ? NickSource.AliasOfFirst
                    : aliasOf == NameField.Last ? NickSource.AliasOfLast
                    : NickSource.Explicit,
            };
            bool anyWanted = false;
            foreach (NameField field in NameFields.All)
            {
                int i = (int)field;
                request.Text[i] = payload.Text[i];
                request.Locked[i] = payload.Locked[i];
                request.Wanted[i] = IsWanted(local[field]);
                anyWanted |= request.Wanted[i];
            }
            if (!anyWanted)
                return local;

            LlmResponse response;
            try
            {
                response = await client.TranslateAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                // Cancelled without our token being cancelled: an HTTP timeout surfacing as an exception.
                response = LlmResponse.Fail(GeminiErrorKind.Timeout);
            }
            catch (Exception e)
            {
                response = LlmResponse.Fail(GeminiErrorKind.InvalidResponse, 0, e.GetType().Name);
            }
            if (response == null)
                response = LlmResponse.Fail(GeminiErrorKind.InvalidResponse, 0, "no response");

            var results = new FieldResult[NameFields.Count];
            foreach (NameField field in NameFields.All)
            {
                FieldResult before = local[field];
                results[(int)field] = IsWanted(before) ? FromResponse(payload, field, before, response) : before;
            }
            FieldResult nick = results[(int)NameField.Nick];
            if (nick.Source == FieldSource.Pending && nick.AliasOf.HasValue)
            {
                NameField target = nick.AliasOf.Value;
                results[(int)NameField.Nick] = Alias(payload, target, results[(int)target]);
            }
            return new NameResult(results[0], results[1], results[2],
                                  response.Success ? GeminiErrorKind.None : response.Error,
                                  response.StatusCode, response.Detail);
        }

        private static bool IsWanted(FieldResult result) => result.Source == FieldSource.Pending && !result.AliasOf.HasValue;

        private FieldResult FromResponse(NameRequestPayload payload, NameField field, FieldResult before, LlmResponse response)
        {
            if (!response.Success)
                return new FieldResult(FieldSource.Failed, null, before.SourceText, error: response.Error);
            NameSpace ns = NameFields.SpaceOf(field);
            string sanitized = NameSanitizer.Sanitize(payload[field], response[field], ns);
            if (sanitized == null)
                return new FieldResult(FieldSource.Failed, null, before.SourceText, error: GeminiErrorKind.InvalidResponse);
            cache.Put(ns, NameKey.Normalize(payload[field]), sanitized, CacheOrigin.Api);
            return new FieldResult(FieldSource.Api, sanitized, before.SourceText);
        }

        private FieldResult ResolveNick(NameRequestPayload payload, FieldResult first, FieldResult last)
        {
            if (payload.Locked[(int)NameField.Nick])
                return Unchanged(payload, NameField.Nick);
            NameField? target = NickAliasResolver.Target(payload);
            if (target.HasValue)
                return Alias(payload, target.Value, target.Value == NameField.First ? first : last);
            return ResolveField(payload, NameField.Nick);
        }

        /// <summary>
        /// The nickname takes the final value of the field it stands for, even when that field is locked as
        /// Chinese; it waits while that field is pending and stays as it is when there is nothing Chinese to mirror.
        /// </summary>
        private static FieldResult Alias(NameRequestPayload payload, NameField target, FieldResult targetResult)
        {
            string sourceText = payload[NameField.Nick]?.Trim();
            if (targetResult.Source == FieldSource.Pending)
                return new FieldResult(FieldSource.Pending, null, sourceText, target);
            string targetValue = targetResult.HasNewValue ? targetResult.Value : payload[target];
            if (!NameSanitizer.ContainsChinese(targetValue))
                return new FieldResult(FieldSource.Unchanged, null, sourceText, target);
            return new FieldResult(FieldSource.Alias, targetValue.Trim(), sourceText, target);
        }

        private FieldResult ResolveField(NameRequestPayload payload, NameField field)
        {
            string text = payload[field];
            if (payload.Locked[(int)field] || string.IsNullOrWhiteSpace(text) || NameSanitizer.ContainsChinese(text))
                return Unchanged(payload, field);
            NameSpace ns = NameFields.SpaceOf(field);
            string sourceText = text.Trim();

            string special = NameSanitizer.Sanitize(text, SpecialFormRule.Apply(text, ns), ns);
            if (special != null)
                return new FieldResult(FieldSource.SpecialForm, special, sourceText);

            string key = NameKey.Normalize(text);
            // Every local hit is re-checked against the real source: lookups ignore case, the source may not.
            if (cache.TryGet(ns, key, out string cached, out _))
            {
                string sanitized = NameSanitizer.Sanitize(text, cached, ns);
                if (sanitized != null)
                    return new FieldResult(FieldSource.PersonalCache, sanitized, sourceText);
            }
            if (dictionary.TryGet(ns, key, out string known))
            {
                string sanitized = NameSanitizer.Sanitize(text, known, ns);
                if (sanitized != null)
                    return new FieldResult(FieldSource.Dictionary, sanitized, sourceText);
            }
            return new FieldResult(FieldSource.Pending, null, sourceText);
        }

        private static FieldResult Unchanged(NameRequestPayload payload, NameField field) =>
            new FieldResult(FieldSource.Unchanged, null, payload[field]?.Trim());
    }
}
