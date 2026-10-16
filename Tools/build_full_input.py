"""Write every token of NameTable/tokens.csv as a translation input (NameTable/translations/full_input.csv).

Ids are T00001... in tokens.csv order (namespace, tier, frequency), so high-priority words are
translated first. Official zh-TW translations stay in the file for comparison but are never sent.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from build_sample import TABLE, load, write_input  # noqa: E402

OUT = os.path.join(TABLE, 'translations', 'full_input.csv')


def main():
    tokens, examples = load()
    write_input(OUT, [(f'T{i:05d}', f"tier{t['tier']}", t) for i, t in enumerate(tokens, 1)], examples)
    print(f'{len(tokens)} tokens -> {OUT}')


if __name__ == '__main__':
    main()
