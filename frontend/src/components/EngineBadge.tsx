// Shows which engine produced the categorization: Hugging Face BART vs the
// built-in internal classifier. `source` is "BART", "Internal", or "Mixed".
export default function EngineBadge({ source }: { source: string }) {
  // "BART (cached)" is BART lineage (the batch source is normalized server-side, but stay robust).
  const isBart = source === 'BART' || source === 'BART (cached)';
  const isMixed = source === 'Mixed';

  if (isMixed) {
    return (
      <span className="engine-badge mixed" title="Some tickets via Hugging Face BART, some via the internal classifier">
        🤗 + ⚙️ <strong>Mixed</strong>
      </span>
    );
  }

  return isBart ? (
    <span className="engine-badge bart" title="Categorized by Facebook BART-large-MNLI on Hugging Face">
      🤗 <strong>BART</strong> · Hugging Face
    </span>
  ) : (
    <span className="engine-badge internal" title="Categorized by the built-in offline classifier">
      ⚙️ <strong>Internal</strong> classifier
    </span>
  );
}
