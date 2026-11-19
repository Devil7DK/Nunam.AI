import type { FileType, MachineSummary } from '@/api/nunam';
import { FileIcon } from '@/components/FileIcon';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import type { Filters } from '@/hooks/useUrlState';
import { cn } from '@/lib/utils';

const types: { value: FileType | ''; label: string }[] = [
  { value: '', label: 'All files' },
  { value: 'document', label: 'Documents' },
  { value: 'image', label: 'Images' },
  { value: 'audio', label: 'Audio' },
  { value: 'video', label: 'Video' },
  { value: 'other', label: 'Other' },
];

const ALL_MACHINES = '__all__';

interface Props {
  filters: Filters;
  machines: MachineSummary[];
  onChange: (patch: Partial<Filters>) => void;
}

function Label({ children, htmlFor }: { children: React.ReactNode; htmlFor?: string }) {
  return (
    <label htmlFor={htmlFor} className="mb-1.5 block text-xs font-medium tracking-wide text-muted-foreground uppercase">
      {children}
    </label>
  );
}

export function FilterPanel({ filters, machines, onChange }: Props) {
  const active = filters.type || filters.machine || filters.ext || filters.from || filters.to;

  return (
    <div className="flex flex-col gap-5">
      <div>
        <Label>Type</Label>
        <ul className="flex flex-wrap gap-1 lg:flex-col lg:gap-0.5">
          {types.map((t) => (
            <li key={t.value}>
              <button
                type="button"
                onClick={() => onChange({ type: t.value })}
                aria-pressed={filters.type === t.value}
                className={cn(
                  'flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-sm text-muted-foreground hover:bg-muted hover:text-foreground',
                  filters.type === t.value && 'bg-muted font-medium text-foreground',
                )}
              >
                {t.value ? <FileIcon type={t.value} className="size-4" /> : <span className="size-4" />}
                {t.label}
              </button>
            </li>
          ))}
        </ul>
      </div>

      <div>
        <Label htmlFor="filter-machine">Machine</Label>
        <Select
          value={filters.machine || ALL_MACHINES}
          onValueChange={(value) => onChange({ machine: value === ALL_MACHINES ? '' : value })}
        >
          <SelectTrigger id="filter-machine" className="w-full">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={ALL_MACHINES}>All machines</SelectItem>
            {machines.map((m) => (
              <SelectItem key={m.id} value={m.name}>
                {m.name} <span className="text-muted-foreground">({m.fileCount})</span>
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      <div>
        <Label htmlFor="filter-ext">Extensions</Label>
        <Input
          id="filter-ext"
          value={filters.ext}
          onChange={(e) => onChange({ ext: e.target.value })}
          placeholder=".pdf, .docx"
        />
      </div>

      <div>
        <Label>Modified</Label>
        <div className="grid grid-cols-2 gap-2 lg:grid-cols-1">
          <Input
            type="date"
            aria-label="Modified from"
            value={filters.from}
            max={filters.to || undefined}
            onChange={(e) => onChange({ from: e.target.value })}
          />
          <Input
            type="date"
            aria-label="Modified to"
            value={filters.to}
            min={filters.from || undefined}
            onChange={(e) => onChange({ to: e.target.value })}
          />
        </div>
      </div>

      {active && (
        <Button
          variant="ghost"
          size="sm"
          className="self-start text-muted-foreground"
          onClick={() => onChange({ type: '', machine: '', ext: '', from: '', to: '' })}
        >
          Clear filters
        </Button>
      )}
    </div>
  );
}
