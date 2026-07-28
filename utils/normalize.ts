const COMBINING_MARKS = /[̀-ͯ]/g;

/** Lowercased, accent-free, punctuation-free form used for comparisons. */
export function normalizeText(value: string): string {
  return value
    .normalize('NFKD')
    .replace(COMBINING_MARKS, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, ' ')
    .trim();
}

/** Digits only, with a leading `+` preserved, for phone comparison. */
export function normalizePhone(value: string): string {
  const trimmed = value.trim();
  const digits = trimmed.replace(/\D/g, '');
  if (!digits) return '';
  return trimmed.startsWith('+') ? `+${digits}` : digits;
}

/** Registrable-ish host: lowercased, `www.` stripped, no port. */
export function normalizeHost(input: string): string {
  try {
    const url = input.includes('://') ? new URL(input) : new URL(`https://${input}`);
    return url.hostname.toLowerCase().replace(/^www\./, '');
  } catch {
    return '';
  }
}

/** Absolute URL with tracking noise and fragments removed; '' when unusable. */
export function normalizeUrl(input: string, base?: string): string {
  const raw = input.trim();
  if (!raw || raw.startsWith('mailto:') || raw.startsWith('tel:')) return '';

  try {
    const url = base ? new URL(raw, base) : new URL(raw.includes('://') ? raw : `https://${raw}`);
    if (url.protocol !== 'http:' && url.protocol !== 'https:') return '';

    url.hash = '';
    for (const key of [...url.searchParams.keys()]) {
      if (/^(utm_|fbclid|gclid|msclkid|mc_eid|mc_cid|ref)$/i.test(key)) {
        url.searchParams.delete(key);
      }
    }
    return url.toString();
  } catch {
    return '';
  }
}

export function isHttpUrl(value: string): boolean {
  try {
    const url = new URL(value);
    return url.protocol === 'http:' || url.protocol === 'https:';
  } catch {
    return false;
  }
}
