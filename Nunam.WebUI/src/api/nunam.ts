export type SearchMode = 'hybrid' | 'keyword' | 'semantic';
export type FileType = 'document' | 'image' | 'audio' | 'video' | 'other';

export interface SearchParams {
  q: string;
  mode: SearchMode;
  type?: FileType;
  machine?: string;
  ext?: string;
  from?: string;
  to?: string;
  limit?: number;
  offset?: number;
}

export interface SearchResult {
  id: string;
  fileName: string;
  path: string;
  machine: string;
  fileType: FileType;
  extension: string | null;
  sizeBytes: number | null;
  modifiedAt: string | null;
  /** Matched words are wrapped in U+E000 … U+E001. */
  snippet: string | null;
  description: string | null;
  hasPreview: boolean;
  score: number | null;
  similarity: number | null;
}

export interface SearchResponse {
  total: number;
  mode: SearchMode;
  semanticUsed: boolean;
  warning: string | null;
  results: SearchResult[];
}

export interface FileDetails {
  id: string;
  fileName: string;
  path: string;
  machine: string;
  fileType: FileType;
  extension: string | null;
  sizeBytes: number | null;
  createdAt: string | null;
  modifiedAt: string | null;
  indexedAt: string;
  description: string | null;
  metadata: Record<string, unknown> | null;
  content: string | null;
  contentLength: number;
  contentTruncated: boolean;
  hasPreview: boolean;
  hasEmbedding: boolean;
}

export interface MachineSummary {
  id: string;
  name: string;
  hostname: string;
  lastSeen: string | null;
  fileCount: number;
  lastIndexedAt: string | null;
}

export interface JobCount {
  kind: 'parse' | 'describe' | 'embed';
  status: 'pending' | 'processing' | 'done' | 'failed';
  count: number;
}

export interface IndexStatus {
  totalFiles: number;
  filesByType: Partial<Record<FileType, number>>;
  described: number;
  embedded: number;
  lastIndexedAt: string | null;
  jobs: JobCount[];
  recentFailures: { id: string; kind: string; machine: string; path: string; stage: string; error: string | null; at: string }[];
  machines: MachineSummary[];
}

export class ApiError extends Error {
  readonly status: number;

  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

async function getJson<T>(url: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(url, { signal, headers: { Accept: 'application/json' } });
  if (!response.ok) {
    let message = `${response.status} ${response.statusText}`;
    try {
      const problem = await response.json();
      const errors = problem.errors ? Object.values(problem.errors).flat().join(' ') : '';
      message = errors || problem.detail || problem.title || message;
    } catch {
      // Not a problem-details body; the status line is all there is.
    }
    throw new ApiError(response.status, message);
  }
  return response.json() as Promise<T>;
}

export function search(params: SearchParams, signal?: AbortSignal): Promise<SearchResponse> {
  const query = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== '') query.set(key, String(value));
  }
  return getJson(`/api/search?${query}`, signal);
}

export const getFile = (id: string, signal?: AbortSignal) => getJson<FileDetails>(`/api/files/${id}`, signal);
export const getMachines = (signal?: AbortSignal) => getJson<MachineSummary[]>('/api/machines', signal);
export const getStatus = (signal?: AbortSignal) => getJson<IndexStatus>('/api/status', signal);

export const previewUrl = (id: string, size?: number) =>
  size ? `/api/files/${id}/preview?size=${size}` : `/api/files/${id}/preview`;

export const MATCH_START = '';
export const MATCH_END = '';
