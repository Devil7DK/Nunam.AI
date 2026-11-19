const sizeUnits = ['B', 'KB', 'MB', 'GB', 'TB'];

export function formatBytes(bytes: number | null): string {
  if (bytes === null) return '';
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < sizeUnits.length - 1) {
    value /= 1024;
    unit++;
  }
  return `${value < 10 && unit > 0 ? value.toFixed(1) : Math.round(value)} ${sizeUnits[unit]}`;
}

const dateFormat = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' });
const dateTimeFormat = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' });

export const formatDate = (iso: string | null) => (iso ? dateFormat.format(new Date(iso)) : '');
export const formatDateTime = (iso: string | null) => (iso ? dateTimeFormat.format(new Date(iso)) : '');

export function formatRelative(iso: string | null): string {
  if (!iso) return 'never';
  const seconds = (Date.now() - new Date(iso).getTime()) / 1000;
  if (seconds < 60) return 'just now';
  if (seconds < 3600) return `${Math.floor(seconds / 60)} min ago`;
  if (seconds < 86400) return `${Math.floor(seconds / 3600)} h ago`;
  return formatDate(iso);
}
