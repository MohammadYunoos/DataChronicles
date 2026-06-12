import axios from 'axios';

export interface OutputTicket {
  id: number;
  applicationName: string;
  incident: string;
  jobName: string;
  category: string;
  confidence: number;
  severity: string;
  sentiment: string;
  source: string;
  batchId: string;
  createdOn: string;
}

export interface CategorySummary {
  category: string;
  count: number;
  percentage: number;
}

export interface CategorizationResult {
  batchId: string;
  totalRecords: number;
  tickets: OutputTicket[];
  summary: CategorySummary[];
  source: string;
  fileName: string;
}

const api = axios.create({ baseURL: '/api' });

export async function uploadAndCategorize(
  file: File,
  connectionId?: string
): Promise<CategorizationResult> {
  const fd = new FormData();
  fd.append('file', file);
  const url = connectionId
    ? `/categorize/upload?connectionId=${encodeURIComponent(connectionId)}`
    : '/categorize/upload';
  const res = await api.post<CategorizationResult>(url, fd, {
    headers: { 'Content-Type': 'multipart/form-data' },
  });
  return res.data;
}

export function downloadUrl(batchId: string): string {
  return `/api/categorize/download/${batchId}`;
}

export function sampleUrl(): string {
  return `/api/categorize/sample`;
}

export async function askAssistant(question: string, batchId?: string): Promise<string> {
  const res = await api.post<{ answer: string }>('/chat', { question, batchId });
  return res.data.answer;
}
