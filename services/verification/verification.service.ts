import '@/lib/server-guard';

import type { BusinessRecord } from '@/types/business';
import { emailVerifier } from './email-verifier';
import { whatsappClassifier } from './whatsapp';

export interface VerificationOutcome {
  emailStatus: BusinessRecord['emailStatus'];
  whatsappStatus: BusinessRecord['whatsappStatus'];
  /** Canonical wa.me link when one can be derived; '' leaves the field alone. */
  whatsappLink: string;
  /** Human-readable notes appended to the record. */
  notes: string[];
}

/**
 * Applies email deliverability and WhatsApp reachability checks to a record.
 *
 * Both checks are non-destructive: the caller decides what to write back. See
 * `email-verifier.ts` and `whatsapp.ts` for exactly what each status does and
 * does not prove.
 */
export const verificationService = {
  async verify(record: BusinessRecord): Promise<VerificationOutcome> {
    const notes: string[] = [];

    const [email, whatsapp] = await Promise.all([
      emailVerifier.verify(record.email),
      Promise.resolve(
        // A wa.me/chat.whatsapp.com link found during enrichment is direct
        // evidence; anything else is inferred from the number's line type.
        whatsappClassifier.assess(record.phone, record.country, record.whatsapp || null),
      ),
    ]);

    if (record.email && email.reason) notes.push(`Email: ${email.reason}`);
    if (whatsapp.reason) notes.push(`WhatsApp: ${whatsapp.reason}`);

    return {
      emailStatus: email.status,
      whatsappStatus: whatsapp.status,
      whatsappLink: whatsapp.link,
      notes,
    };
  },

  /** Verifies and writes the results onto the record in place. */
  async applyTo(record: BusinessRecord): Promise<VerificationOutcome> {
    const outcome = await this.verify(record);

    record.emailStatus = outcome.emailStatus;
    record.whatsappStatus = outcome.whatsappStatus;

    // Only fill the WhatsApp column when it's empty — never overwrite a link the
    // business published with one we derived.
    if (!record.whatsapp && outcome.whatsappLink) {
      record.whatsapp = outcome.whatsappLink;
    }

    return outcome;
  },
};
