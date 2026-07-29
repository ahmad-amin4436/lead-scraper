import '@/lib/server-guard';

import { parsePhoneNumberFromString, type CountryCode } from 'libphonenumber-js/max';

import type { WhatsAppStatus } from '@/types/business';

/**
 * WhatsApp reachability.
 *
 * There is no legitimate way to test whether an arbitrary number is registered
 * on WhatsApp. The Business API only answers for numbers you own, and the
 * unofficial endpoints that claim to do it violate WhatsApp's terms and get
 * numbers banned. So this classifier never asserts registration — it reports
 * the strongest evidence available:
 *
 *   confirmed — the business published a WhatsApp link on its own website.
 *               This is direct evidence and the only reliable signal.
 *   likely    — the number is a valid mobile line. Mobile numbers carry the
 *               overwhelming majority of WhatsApp accounts, but this is an
 *               inference, not a check.
 *   unlikely  — the number is a landline or otherwise not a mobile line.
 *   none      — no usable phone number on the record.
 *
 * Line type comes from libphonenumber's full metadata, so it is accurate
 * per-country rather than guessed from prefixes.
 */

export interface WhatsAppAssessment {
  status: WhatsAppStatus;
  /** Canonical click-to-chat link, or '' when there is nothing to link to. */
  link: string;
  reason: string;
}

const NONE: WhatsAppAssessment = { status: 'none', link: '', reason: '' };

/** Builds a wa.me link from an E.164 number (digits only, no leading +). */
export function buildWaMeLink(e164: string): string {
  const digits = e164.replace(/\D/g, '');
  return digits.length >= 7 ? `https://wa.me/${digits}` : '';
}

export const whatsappClassifier = {
  /**
   * @param phone       Phone number as stored on the record.
   * @param countryCode ISO-3166 alpha-2, used to parse national-format numbers.
   * @param confirmedLink WhatsApp URL discovered on the business website, if any.
   */
  assess(
    phone: string,
    countryCode: string,
    confirmedLink?: string | null,
  ): WhatsAppAssessment {
    // A link the business published itself beats any inference we could make.
    if (confirmedLink) {
      return {
        status: 'confirmed',
        link: confirmedLink,
        reason: 'WhatsApp link published on the business website',
      };
    }

    const raw = phone.trim();
    if (!raw) return NONE;

    const region = /^[A-Z]{2}$/.test(countryCode.toUpperCase())
      ? (countryCode.toUpperCase() as CountryCode)
      : undefined;

    const parsed = parsePhoneNumberFromString(raw, region);
    if (!parsed || !parsed.isValid()) {
      return { status: 'unlikely', link: '', reason: 'Phone number could not be validated' };
    }

    const type = parsed.getType();
    const link = buildWaMeLink(parsed.number);

    // FIXED_LINE_OR_MOBILE covers countries (notably the US) where the
    // numbering plan makes the two indistinguishable — treat as possible.
    if (type === 'MOBILE' || type === 'FIXED_LINE_OR_MOBILE') {
      return {
        status: 'likely',
        link,
        reason:
          type === 'MOBILE'
            ? 'Mobile number — WhatsApp likely but unverified'
            : 'Mobile or landline — WhatsApp possible but unverified',
      };
    }

    if (type === undefined) {
      return {
        status: 'unlikely',
        link: '',
        reason: 'Line type unknown for this number',
      };
    }

    return {
      status: 'unlikely',
      link: '',
      reason: `Line type is ${type.toLowerCase().replace(/_/g, ' ')}`,
    };
  },
};
