import { useRef, useState } from 'react';

interface Msg {
  role: 'user' | 'ai';
  text: string;
}

export default function ChatPanel({
  ask,
  ready,
}: {
  ask: (q: string) => Promise<string>;
  ready: boolean;
}) {
  const [messages, setMessages] = useState<Msg[]>([]);
  const [input, setInput] = useState('');
  const [sending, setSending] = useState(false);
  const listRef = useRef<HTMLDivElement>(null);

  const send = async () => {
    const q = input.trim();
    if (!q || sending) return;
    setMessages((m) => [...m, { role: 'user', text: q }]);
    setInput('');
    setSending(true);
    try {
      const answer = await ask(q);
      setMessages((m) => [...m, { role: 'ai', text: answer }]);
    } catch {
      setMessages((m) => [...m, { role: 'ai', text: 'Sorry, I could not reach the assistant.' }]);
    } finally {
      setSending(false);
      setTimeout(() => listRef.current?.scrollTo(0, listRef.current.scrollHeight), 50);
    }
  };

  return (
    <section className="card chat-card">
      <div className="chat-header">Ask Our AI Assistant</div>

      <div className="chat-body" ref={listRef}>
        {messages.length === 0 && (
          <p className="muted chat-hint">
            {ready
              ? 'Try: “How many tickets were categorized?”, “Most common category?”, “Severity breakdown”, “Any duplicates?”'
              : 'Upload and categorize a file, then ask me about the results.'}
          </p>
        )}
        {messages.map((m, i) => (
          <div key={i} className={`bubble ${m.role}`}>
            {m.text.split('\n').map((line, j) => (
              <div key={j}>{line}</div>
            ))}
          </div>
        ))}
      </div>

      <div className="chat-input">
        <input
          placeholder="Ask me anything..."
          value={input}
          onChange={(e) => setInput(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && send()}
        />
        <button className="primary" onClick={send} disabled={sending || !input.trim()}>
          {sending ? '…' : 'Send'}
        </button>
      </div>
    </section>
  );
}
