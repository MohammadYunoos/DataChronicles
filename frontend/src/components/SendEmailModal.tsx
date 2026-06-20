import { useMemo, useState } from 'react';
import { OutputTicket, sendEmailReport } from '../api';

/** Confidence below this is treated as "needs review" (keep in sync with the backend). */
export const LOW_CONFIDENCE = 0.6;

const STORAGE_KEY = 'dc.emailRecipient';
const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export default function SendEmailModal({
  batchId,
  tickets,
  onClose,
  onSent,
}: {
  batchId: string;
  tickets: OutputTicket[];
  onClose: () => void;
  onSent: (message: string) => void;
}) {
  const [email, setEmail] = useState(() => localStorage.getItem(STORAGE_KEY) ?? '');
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [confirming, setConfirming] = useState(false);

  const lowCount = useMemo(
    () => tickets.filter((t) => t.confidence < LOW_CONFIDENCE).length,
    [tickets]
  );

  const doSend = async () => {
    setSending(true);
    setError(null);
    try {
      localStorage.setItem(STORAGE_KEY, email);
      const res = await sendEmailReport(batchId, email);
      if (res.success) {
        onSent(`✅ ${res.message}`);
        onClose();
      } else {
        setError(res.message);
      }
    } catch (e: any) {
      setError(e?.response?.data?.error ?? 'Failed to send the email. Please try again.');
    } finally {
      setSending(false);
    }
  };

  // Human-in-the-loop: validate, then warn on low-confidence rows before sending.
  const onSendClick = () => {
    if (!EMAIL_RE.test(email.trim())) {
      setError('Please enter a valid email address.');
      return;
    }
    setError(null);
    if (lowCount > 0 && !confirming) {
      setConfirming(true);
      return;
    }
    void doSend();
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" onClick={(e) => e.stopPropagation()}>
        <h2>Email categorized report</h2>
        <p className="muted">
          The generated Excel file and a summary will be sent to the address below.
        </p>

        <label htmlFor="email-to">Recipient email</label>
        <input
          id="email-to"
          type="email"
          placeholder="name@example.com"
          value={email}
          disabled={sending}
          onChange={(e) => {
            setEmail(e.target.value);
            setConfirming(false);
          }}
          onKeyDown={(e) => e.key === 'Enter' && onSendClick()}
        />

        {confirming && lowCount > 0 && (
          <div className="warn">
            ⚠️ {lowCount} row{lowCount === 1 ? '' : 's'} have confidence below{' '}
            {Math.round(LOW_CONFIDENCE * 100)}% and may need review. Send anyway?
          </div>
        )}

        {error && <p className="error">{error}</p>}

        <div className="modal-actions">
          <button className="ghost" onClick={onClose} disabled={sending}>
            Cancel
          </button>
          <button className="primary" onClick={onSendClick} disabled={sending}>
            {sending ? 'Sending…' : confirming ? 'Confirm & Send' : 'Send'}
          </button>
        </div>
      </div>
    </div>
  );
}
