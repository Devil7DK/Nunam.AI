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
  /** The user may change who can see this machine. */
  canShare: boolean;
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
  /** Per-field validation messages, keyed by the request's camelCase field names. */
  readonly fieldErrors: Record<string, string[]>;

  constructor(status: number, message: string, fieldErrors: Record<string, string[]> = {}) {
    super(message);
    this.status = status;
    this.fieldErrors = fieldErrors;
  }
}

/** Fired when the API says the session is gone, so the app can return to sign-in. */
export const UNAUTHORIZED_EVENT = 'nunam:unauthorized';

async function request<T>(url: string, init: RequestInit = {}): Promise<T> {
  const response = await fetch(url, {
    ...init,
    credentials: 'same-origin',
    headers: {
      Accept: 'application/json',
      ...(init.body !== undefined && { 'Content-Type': 'application/json' }),
      // The API refuses state-changing requests without it (CSRF protection).
      ...(init.method && init.method !== 'GET' && { 'X-Nunam-Request': '1' }),
      ...init.headers,
    },
  });

  if (response.status === 401 && !url.startsWith('/api/auth/')) {
    window.dispatchEvent(new Event(UNAUTHORIZED_EVENT));
  }

  if (!response.ok) {
    let message = `${response.status} ${response.statusText}`;
    let fieldErrors: Record<string, string[]> = {};
    try {
      const problem = await response.json();
      fieldErrors = problem.errors ?? {};
      const errors = Object.values(fieldErrors).flat().join(' ');
      message = errors || problem.detail || problem.title || message;
    } catch {
      // Not a problem-details body; the status line is all there is.
    }
    if (response.status === 429) message = 'Too many attempts. Wait a minute and try again.';
    throw new ApiError(response.status, message, fieldErrors);
  }

  if (response.status === 204) return undefined as T;
  return response.json() as Promise<T>;
}

const getJson = <T,>(url: string, signal?: AbortSignal) => request<T>(url, { signal });

const send = <T = void,>(method: 'POST' | 'PUT' | 'PATCH' | 'DELETE', url: string, body?: unknown) =>
  request<T>(url, { method, body: body === undefined ? undefined : JSON.stringify(body) });

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

export type UserRole = 'admin' | 'member';
export type MachineRole = 'owner' | 'member';

export interface UserInfo {
  id: string;
  userName: string;
  displayName: string;
  role: UserRole;
  mustChangePassword: boolean;
}

export interface AuthState {
  setupRequired: boolean;
  user: UserInfo | null;
}

export interface SessionInfo {
  id: string;
  created: string;
  lastSeen: string;
  userAgent: string | null;
  ipAddress: string | null;
  current: boolean;
}

export interface UserSummary {
  id: string;
  userName: string;
  displayName: string;
  role: UserRole;
  disabled: boolean;
  mustChangePassword: boolean;
  lastSignIn: string | null;
  created: string;
}

export interface DirectoryEntry {
  id: string;
  userName: string;
  displayName: string;
}

export interface MachineAccessEntry {
  userId: string;
  userName: string;
  displayName: string;
  role: MachineRole;
}

export const getAuthState = () => getJson<AuthState>('/api/auth/state');
export const setup = (body: { code: string; userName: string; displayName: string; password: string }) =>
  send<UserInfo>('POST', '/api/auth/setup', body);
export const signIn = (body: { userName: string; password: string }) => send<UserInfo>('POST', '/api/auth/sign-in', body);
export const signOut = (everywhere = false) =>
  send('POST', everywhere ? '/api/auth/sign-out?everywhere=true' : '/api/auth/sign-out');
export const changePassword = (body: { currentPassword: string; newPassword: string }) =>
  send<UserInfo>('POST', '/api/auth/password', body);
export const getSessions = () => getJson<SessionInfo[]>('/api/auth/sessions');
export const endSession = (id: string) => send('DELETE', `/api/auth/sessions/${id}`);
export const endOtherSessions = () => send('POST', '/api/auth/sessions/end-others');

export const getUsers = () => getJson<UserSummary[]>('/api/users');
export const getDirectory = () => getJson<DirectoryEntry[]>('/api/users/directory');
export const createUser = (body: { userName: string; displayName: string; role: UserRole; temporaryPassword: string }) =>
  send<UserSummary>('POST', '/api/users', body);
export const updateUser = (id: string, body: { displayName?: string; role?: UserRole; disabled?: boolean }) =>
  send<UserSummary>('PATCH', `/api/users/${id}`, body);
export const resetPassword = (id: string, temporaryPassword: string) =>
  send('POST', `/api/users/${id}/password`, { temporaryPassword });
export const deleteUser = (id: string) => send('DELETE', `/api/users/${id}`);

export const getMachineAccess = (machineId: string) => getJson<MachineAccessEntry[]>(`/api/machines/${machineId}/access`);
export const setMachineAccess = (machineId: string, userId: string, role: MachineRole) =>
  send('PUT', `/api/machines/${machineId}/access/${userId}`, { role });
export const removeMachineAccess = (machineId: string, userId: string) =>
  send('DELETE', `/api/machines/${machineId}/access/${userId}`);

/** A random temporary password for an admin to hand over; the user must change it at first sign-in. */
export function generateTemporaryPassword(): string {
  const alphabet = 'abcdefghjkmnpqrstuvwxyz23456789';
  // Bytes past the last whole multiple of the alphabet are dropped so every letter is equally likely.
  const limit = 256 - (256 % alphabet.length);
  const chars: string[] = [];
  while (chars.length < 15) {
    for (const b of crypto.getRandomValues(new Uint8Array(32))) {
      if (b < limit && chars.length < 15) chars.push(alphabet[b % alphabet.length]);
    }
  }
  return [0, 5, 10].map((i) => chars.slice(i, i + 5).join('')).join('-');
}

export type IndexerState = 'running' | 'scanning' | 'paused';
export type IndexerCommandKind = 'restart' | 'pause' | 'resume' | 'retryFailed';
export type LogLevel = 'trace' | 'debug' | 'information' | 'warning' | 'error' | 'critical';

export interface IndexerListItem {
  machineId: string;
  name: string;
  hostname: string;
  fileCount: number;
  online: boolean;
  state: IndexerState | null;
  reportedAt: string | null;
  version: string | null;
  remoteManagement: boolean;
  queued: number;
  failed: number;
  canManage: boolean;
}

export interface ConfigEntry {
  key: string;
  value: string | null;
  source: 'default' | 'local' | 'remote';
}

export interface IndexerStatusInfo {
  online: boolean;
  reportedAt: string;
  processStartedAt: string;
  startedAt: string;
  version: string;
  operatingSystem: string;
  serviceAccount: string | null;
  state: IndexerState;
  remoteManagement: boolean;
  appliedRevision: number | null;
  scanStartedAt: string | null;
  scanFinishedAt: string | null;
  watchPaths: { path: string; available: boolean; problem: string | null }[];
  /** Only for admins and the machine's owners. */
  config: ConfigEntry[] | null;
}

/** A null field keeps the machine's own value. */
export interface RemoteIndexerSettings {
  machineName?: string | null;
  watchPaths?: string[] | null;
  excludePatterns?: string[] | null;
  skipSystemFolders?: boolean | null;
  maxFileSizeMb?: number | null;
  maxPreviewMb?: number | null;
  enableOcr?: boolean | null;
  ocrLanguage?: string | null;
  parsers?: Record<string, string[]> | null;
  ocrEngine?: string | null;
  pdfRenderer?: string | null;
}

export interface IndexerSettingsInfo {
  settings: RemoteIndexerSettings;
  revision: number;
  updatedBy: string;
  updatedAt: string;
}

export interface IndexerCommandInfo {
  id: string;
  kind: IndexerCommandKind;
  status: 'pending' | 'done' | 'failed' | 'rejected';
  requestedBy: string;
  requestedAt: string;
  completedAt: string | null;
  result: string | null;
}

export interface IndexerDetails {
  machineId: string;
  name: string;
  hostname: string;
  fileCount: number;
  lastIndexedAt: string | null;
  canManage: boolean;
  status: IndexerStatusInfo | null;
  jobs: JobCount[];
  settings: IndexerSettingsInfo | null;
  commands: IndexerCommandInfo[] | null;
}

export interface IndexerLogEntry {
  id: string;
  at: string;
  level: LogLevel;
  category: string;
  message: string;
  exception: string | null;
}

export const getIndexers = (signal?: AbortSignal) => getJson<IndexerListItem[]>('/api/indexers', signal);
export const getIndexer = (machineId: string, signal?: AbortSignal) =>
  getJson<IndexerDetails>(`/api/machines/${machineId}/indexer`, signal);
export function getIndexerLogs(machineId: string, options: { level?: LogLevel; before?: string; limit?: number } = {}, signal?: AbortSignal) {
  const query = new URLSearchParams();
  if (options.level) query.set('level', options.level);
  if (options.before) query.set('before', options.before);
  if (options.limit) query.set('limit', String(options.limit));
  return getJson<IndexerLogEntry[]>(`/api/machines/${machineId}/indexer/logs?${query}`, signal);
}
export const sendIndexerCommand = (machineId: string, kind: IndexerCommandKind) =>
  send<IndexerCommandInfo>('POST', `/api/machines/${machineId}/indexer/commands`, { kind });
export const saveIndexerSettings = (machineId: string, settings: RemoteIndexerSettings) =>
  send<IndexerSettingsInfo>('PUT', `/api/machines/${machineId}/indexer/settings`, settings);
