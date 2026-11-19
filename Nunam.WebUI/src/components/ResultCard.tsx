import { previewUrl, type SearchResult } from '@/api/nunam';
import { CopyPathButton } from '@/components/CopyPathButton';
import { FileIcon } from '@/components/FileIcon';
import { Highlighted } from '@/components/Highlighted';
import { Badge } from '@/components/ui/badge';
import { formatBytes, formatDate } from '@/lib/format';

export function ResultCard({ result, onOpen }: { result: SearchResult; onOpen: (result: SearchResult) => void }) {
  const folder = result.path.slice(0, result.path.length - result.fileName.length);
  const meta = [formatBytes(result.sizeBytes), formatDate(result.modifiedAt)].filter(Boolean).join(' · ');

  return (
    <li className="group relative flex gap-3 border-b px-1 py-3 last:border-b-0 hover:bg-muted/40 sm:gap-4 sm:px-2">
      <div className="flex size-14 shrink-0 items-center justify-center overflow-hidden rounded-md border bg-muted/50 sm:size-16">
        {result.hasPreview ? (
          <img
            src={previewUrl(result.id, 128)}
            alt=""
            loading="lazy"
            // Photos fill the tile; a rendered page is mostly margin, so it's shown whole.
            className={result.fileType === 'image' || result.fileType === 'video' ? 'size-full object-cover' : 'size-full bg-white object-contain'}
          />
        ) : (
          <FileIcon type={result.fileType} className="size-6 text-muted-foreground" />
        )}
      </div>

      <div className="min-w-0 flex-1">
        <div className="flex items-baseline gap-2">
          <button
            type="button"
            onClick={() => onOpen(result)}
            className="truncate text-left font-medium text-foreground after:absolute after:inset-0 hover:underline focus-visible:underline focus-visible:outline-none"
          >
            {result.fileName}
          </button>
          <Badge variant="outline" className="shrink-0 font-normal text-muted-foreground">
            {result.machine}
          </Badge>
        </div>

        <div className="flex items-center gap-1 text-xs text-muted-foreground">
          <span className="truncate font-mono" title={result.path}>
            {folder}
          </span>
          <span className="relative z-10 opacity-0 transition-opacity group-hover:opacity-100 focus-within:opacity-100">
            <CopyPathButton path={result.path} />
          </span>
        </div>

        {result.snippet ? (
          <p className="mt-1 line-clamp-2 text-sm text-foreground/80">
            <Highlighted text={result.snippet} />
          </p>
        ) : (
          result.description && <p className="mt-1 line-clamp-2 text-sm text-foreground/80">{result.description}</p>
        )}

        {meta && <p className="mt-1 text-xs text-muted-foreground tabular-nums">{meta}</p>}
      </div>
    </li>
  );
}
