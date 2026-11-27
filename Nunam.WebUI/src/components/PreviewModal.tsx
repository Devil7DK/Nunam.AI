import { useEffect, useState } from 'react';
import { getFile, previewUrl, type FileDetails, type SearchResult } from '@/api/nunam';
import { CopyPathButton } from '@/components/CopyPathButton';
import { FileIcon } from '@/components/FileIcon';
import { Badge } from '@/components/ui/badge';
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { Skeleton } from '@/components/ui/skeleton';
import { formatBytes, formatDateTime } from '@/lib/format';

const metadataLabels: Record<string, string> = {
  pages: 'Pages',
  title: 'Title',
  author: 'Author',
  subject: 'Subject',
  words: 'Words',
  sheets: 'Sheets',
  slides: 'Slides',
  encrypted: 'Encrypted',
  width: 'Width',
  height: 'Height',
  cameraMake: 'Camera make',
  cameraModel: 'Camera model',
  takenAt: 'Taken',
  latitude: 'Latitude',
  longitude: 'Longitude',
  artists: 'Artists',
  album: 'Album',
  year: 'Year',
  track: 'Track',
  genres: 'Genres',
  durationSeconds: 'Duration',
  bitrateKbps: 'Bitrate',
  sampleRate: 'Sample rate',
  channels: 'Channels',
  frameRate: 'Frame rate',
  videoCodec: 'Video codec',
  audioCodec: 'Audio codec',
  container: 'Container',
  createdAt: 'Recorded',
};

function formatMetadata(key: string, value: unknown): string {
  if (Array.isArray(value)) return value.join(', ');
  if (key === 'durationSeconds' && typeof value === 'number') {
    const minutes = Math.floor(value / 60);
    return `${minutes}:${String(Math.round(value % 60)).padStart(2, '0')}`;
  }
  if (key === 'bitrateKbps') return `${value} kbps`;
  if (key === 'sampleRate') return `${value} Hz`;
  if (key === 'width' || key === 'height') return `${value} px`;
  if (typeof value === 'boolean') return value ? 'Yes' : 'No';
  return String(value);
}

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <>
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="min-w-0 break-words">{children}</dd>
    </>
  );
}

export function PreviewModal({ result, onClose }: { result: SearchResult | null; onClose: () => void }) {
  const [loaded, setLoaded] = useState<{ id: string; details: FileDetails | null; error: string | null } | null>(null);

  useEffect(() => {
    if (!result) return;
    const controller = new AbortController();
    getFile(result.id, controller.signal)
      .then((details) => setLoaded({ id: result.id, details, error: null }))
      .catch(() => {
        if (!controller.signal.aborted) {
          setLoaded({ id: result.id, details: null, error: 'Could not load the file details.' });
        }
      });
    return () => controller.abort();
  }, [result]);

  const current = loaded && result && loaded.id === result.id ? loaded : null;
  const details = current?.details ?? null;
  const error = current?.error ?? null;

  const location = details?.metadata?.latitude !== undefined && details?.metadata?.longitude !== undefined
    ? `${details.metadata.latitude},${details.metadata.longitude}`
    : null;

  return (
    <Dialog open={result !== null} onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="flex max-h-[90vh] flex-col gap-0 overflow-hidden p-0 sm:max-w-3xl">
        {result && (
          <>
            <DialogHeader className="border-b px-5 py-4 text-left">
              <DialogTitle className="flex items-center gap-2 pr-8">
                <FileIcon type={result.fileType} className="size-5 shrink-0 text-muted-foreground" />
                <span className="truncate">{result.fileName}</span>
              </DialogTitle>
              <DialogDescription className="flex items-center gap-1 font-mono text-xs">
                <Badge variant="outline" className="font-sans font-normal">{result.machine}</Badge>
                <span className="truncate" title={result.path}>{result.path}</span>
                <CopyPathButton path={result.path} />
              </DialogDescription>
            </DialogHeader>

            <div className="flex-1 overflow-y-auto px-5 py-4">
              {result.hasPreview && (
                <img
                  src={previewUrl(result.id, 1024)}
                  alt={details?.description ?? result.fileName}
                  className="mx-auto mb-4 max-h-[50vh] rounded-md border object-contain"
                />
              )}

              {error && <p className="text-sm text-destructive">{error}</p>}
              {!details && !error && (
                <div className="space-y-2">
                  <Skeleton className="h-4 w-2/3" />
                  <Skeleton className="h-4 w-1/2" />
                  <Skeleton className="h-24 w-full" />
                </div>
              )}

              {details && (
                <div className="space-y-5">
                  {details.description && (
                    <section>
                      <h3 className="mb-1 text-xs font-medium tracking-wide text-muted-foreground uppercase">What it shows</h3>
                      <p className="text-sm leading-relaxed">{details.description}</p>
                      {details.descriptionModel && (
                        <p className="mt-1 text-xs text-muted-foreground">
                          Written by {details.descriptionModel}
                          {details.descriptionProvider && ` through ${details.descriptionProvider}`}
                          {details.describedAt && `, ${formatDateTime(details.describedAt)}`}
                        </p>
                      )}
                    </section>
                  )}

                  <section>
                    <h3 className="mb-1.5 text-xs font-medium tracking-wide text-muted-foreground uppercase">Details</h3>
                    <dl className="grid grid-cols-[minmax(7rem,auto)_1fr] gap-x-4 gap-y-1 text-sm">
                      <Row label="Size">{formatBytes(details.sizeBytes)}</Row>
                      <Row label="Modified">{formatDateTime(details.modifiedAt)}</Row>
                      <Row label="Created">{formatDateTime(details.createdAt)}</Row>
                      <Row label="Indexed">{formatDateTime(details.indexedAt)}</Row>
                      {Object.entries(details.metadata ?? {})
                        .filter(([key]) => key !== 'latitude' && key !== 'longitude')
                        .map(([key, value]) => (
                          <Row key={key} label={metadataLabels[key] ?? key}>{formatMetadata(key, value)}</Row>
                        ))}
                      {location && (
                        <Row label="Location">
                          <a
                            href={`https://www.openstreetmap.org/?mlat=${details.metadata!.latitude}&mlon=${details.metadata!.longitude}#map=15/${details.metadata!.latitude}/${details.metadata!.longitude}`}
                            target="_blank"
                            rel="noreferrer"
                            className="underline underline-offset-2"
                          >
                            {location}
                          </a>
                        </Row>
                      )}
                    </dl>
                  </section>

                  {details.content && (
                    <section>
                      <h3 className="mb-1.5 text-xs font-medium tracking-wide text-muted-foreground uppercase">
                        Text{details.contentTruncated && ` (first ${details.content.length.toLocaleString()} of ${details.contentLength.toLocaleString()} characters)`}
                      </h3>
                      <pre className="max-h-80 overflow-auto rounded-md border bg-muted/40 p-3 font-mono text-xs leading-relaxed whitespace-pre-wrap">
                        {details.content}
                      </pre>
                    </section>
                  )}
                </div>
              )}
            </div>
          </>
        )}
      </DialogContent>
    </Dialog>
  );
}
