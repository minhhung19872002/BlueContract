/**
 * ADR-060: the interface is Vietnamese only and its strings live next to their components. This test is the guard
 * that replaces a translation catalogue: no visible text (JSX text, and the props people read) may be English UI
 * vocabulary. Code identifiers, CSS classes and comments are not checked.
 */
const SOURCES = import.meta.glob(['../**/*.tsx', '!../**/*.test.tsx'], {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

/** English words that would only appear in a string meant for people. */
const ENGLISH =
  /\b(OK|Cancel|Submit|Save|Delete|Edit|Search|Loading|Error|Upload|Download|Export|Import|Settings|Login|Logout|Password|Username|Placeholder|Template|Workflow|Dashboard|Details|Close|Back|Next|Previous|Confirm|Required|Invalid|Failed|Success|Warning|Unknown|Untitled|Select|Create|Update|Remove|Add new)\b/;

/** Props whose value is read by people. */
const VISIBLE_PROPS =
  /\b(title|label|message|placeholder|okText|cancelText|description|subtitle|tooltip|emptyTitle|emptyDescription|searchPlaceholder|extra|aria-label|sub|code)=(["'])([^"']*)\2/g;

/** Text between JSX tags: ">Something<" (not expressions). */
const JSX_TEXT = />\s*([^<>{}\n]*[A-Za-zÀ-ỹ][^<>{}\n]*)\s*</g;

describe('the interface is Vietnamese only (ADR-060)', () => {
  it('scans the components', () => {
    expect(Object.keys(SOURCES).length).toBeGreaterThan(50);
  });

  it('has no English UI words in visible text', () => {
    const offenders: string[] = [];
    for (const [file, source] of Object.entries(SOURCES)) {
      const code = source.replace(/\/\*[\s\S]*?\*\//g, '').replace(/^\s*\/\/.*$/gm, '');
      for (const m of code.matchAll(VISIBLE_PROPS)) {
        if (ENGLISH.test(m[3]!)) offenders.push(`${file}: ${m[1]}="${m[3]}"`);
      }
      for (const m of code.matchAll(JSX_TEXT)) {
        const text = m[1]!.trim();
        // Skip TypeScript generics / comparisons that look like tags ("<T>", "a < b").
        if (/[=;()&|]/.test(text)) continue;
        if (ENGLISH.test(text)) offenders.push(`${file}: "${text}"`);
      }
    }
    expect(offenders).toEqual([]);
  });
});
