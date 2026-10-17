/** Vietnamese-insensitive matching: "hop dong" finds "Hợp đồng". */
export const foldVietnamese = (s: string) =>
  s
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .replace(/đ/g, 'd')
    .replace(/Đ/g, 'D')
    .toLowerCase();

/** True when every word of the query appears in one of the fields (accents and case ignored). */
export function matchesSearch(query: string, ...fields: (string | null | undefined)[]) {
  const words = foldVietnamese(query).split(/\s+/).filter(Boolean);
  if (words.length === 0) return true;
  const haystack = foldVietnamese(fields.filter(Boolean).join(' '));
  return words.every((w) => haystack.includes(w));
}
