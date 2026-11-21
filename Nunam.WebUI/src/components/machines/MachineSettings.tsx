import { useId, useState } from 'react';
import { ApiError, saveIndexerSettings, type ConfigEntry, type IndexerDetails, type RemoteIndexerSettings } from '@/api/nunam';
import { Heading } from '@/components/machines/MachineOverview';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { fieldMessages } from '@/lib/forms';
import { formatDateTime } from '@/lib/format';

type Kind = 'text' | 'lines' | 'number' | 'bool';
interface FieldDef {
  key: keyof RemoteIndexerSettings;
  option: string;
  label: string;
  kind: Kind;
  hint?: string;
}

const fields: FieldDef[] = [
  { key: 'machineName', option: 'MachineName', label: 'Machine name', kind: 'text', hint: 'Shown in the UI; the machine is still matched by hostname.' },
  { key: 'watchPaths', option: 'WatchPaths', label: 'Watch paths', kind: 'lines', hint: 'One full path per line. Files under a path you remove are dropped from the index. On Windows, run Configure Nunam Indexer on the machine so the service may read a new path.' },
  { key: 'excludePatterns', option: 'ExcludePatterns', label: 'Exclude patterns', kind: 'lines', hint: 'One per line; replaces the whole list. /name/ skips folders with that name, *.ext skips files.' },
  { key: 'skipSystemFolders', option: 'SkipSystemFolders', label: 'Skip system folders', kind: 'bool' },
  { key: 'maxFileSizeMb', option: 'MaxFileSizeMb', label: 'Largest file to read (MB)', kind: 'number' },
  { key: 'maxPreviewMb', option: 'MaxPreviewMb', label: 'Largest image to preview (MB)', kind: 'number' },
  { key: 'enableOcr', option: 'EnableOcr', label: 'Read text in images (OCR)', kind: 'bool' },
  { key: 'ocrLanguage', option: 'OcrLanguage', label: 'OCR languages', kind: 'text', hint: 'Such as eng or eng+tam; each needs its language data on the machine.' },
];

type FormState = Record<string, { set: boolean; value: string }>;

function toText(value: unknown): string {
  if (value === null || value === undefined) return '';
  if (Array.isArray(value)) return value.join('\n');
  return String(value);
}

function initialState(details: IndexerDetails): FormState {
  const remote = details.settings?.settings ?? {};
  const effective = new Map((details.status?.config ?? []).map((c) => [c.key, c.value]));
  return Object.fromEntries(fields.map((f) => {
    const own = remote[f.key];
    return [f.key, own === null || own === undefined
      ? { set: false, value: effective.get(`Indexer:${f.option}`) ?? '' }
      : { set: true, value: toText(own) }];
  }));
}

function toSettings(state: FormState): RemoteIndexerSettings {
  const settings: Record<string, unknown> = {};
  for (const f of fields) {
    const { set, value } = state[f.key];
    if (!set) { settings[f.key] = null; continue; }
    settings[f.key] = f.kind === 'lines' ? value.split('\n').map((l) => l.trim()).filter(Boolean)
      : f.kind === 'number' ? Number(value)
      : f.kind === 'bool' ? value === 'true'
      : value;
  }
  return settings as RemoteIndexerSettings;
}

function SettingRow({ field, state, error, onChange }: {
  field: FieldDef;
  state: { set: boolean; value: string };
  error?: string;
  onChange: (next: { set: boolean; value: string }) => void;
}) {
  const id = useId();
  const common = { id, disabled: !state.set, 'aria-invalid': error ? true : undefined, 'aria-describedby': `${id}-help` };
  return (
    <div className="grid gap-1.5 border-b pb-4 last:border-b-0 sm:grid-cols-[14rem_1fr] sm:gap-4">
      <div className="flex items-start gap-2 pt-1.5">
        <input
          type="checkbox"
          id={`${id}-set`}
          className="mt-0.5 size-4 accent-foreground"
          checked={state.set}
          onChange={(e) => onChange({ ...state, set: e.target.checked })}
        />
        <label htmlFor={`${id}-set`} className="text-sm leading-tight">
          <span className="font-medium">{field.label}</span>
          <span className="block text-xs text-muted-foreground">{state.set ? 'Set here' : "The machine's own value"}</span>
        </label>
      </div>
      <div className="grid gap-1">
        <label htmlFor={id} className="sr-only">{field.label}</label>
        {field.kind === 'lines' ? (
          <textarea
            {...common}
            rows={Math.min(8, Math.max(3, state.value.split('\n').length))}
            spellCheck={false}
            className="w-full rounded-md border bg-transparent px-3 py-2 font-mono text-xs shadow-xs outline-none focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50 disabled:opacity-60 aria-invalid:border-destructive"
            value={state.value}
            onChange={(e) => onChange({ ...state, value: e.target.value })}
          />
        ) : field.kind === 'bool' ? (
          <select
            {...common}
            className="h-9 w-32 rounded-md border bg-transparent px-2 text-sm shadow-xs outline-none focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50 disabled:opacity-60"
            value={state.value === 'true' ? 'true' : 'false'}
            onChange={(e) => onChange({ ...state, value: e.target.value })}
          >
            <option value="true">Yes</option>
            <option value="false">No</option>
          </select>
        ) : (
          <Input
            {...common}
            type={field.kind === 'number' ? 'number' : 'text'}
            className={field.kind === 'number' ? 'w-32' : undefined}
            value={state.value}
            onChange={(e) => onChange({ ...state, value: e.target.value })}
          />
        )}
        <p id={`${id}-help`} className={`text-xs ${error ? 'text-destructive' : 'text-muted-foreground'}`}>{error ?? field.hint}</p>
      </div>
    </div>
  );
}

const sourceLabels: Record<ConfigEntry['source'], string> = { default: 'default', local: 'machine', remote: 'set here' };

function InEffect({ config }: { config: ConfigEntry[] }) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-left text-sm">
        <thead className="text-xs text-muted-foreground">
          <tr className="border-b">
            <th className="py-1.5 pr-4 font-medium">Setting</th>
            <th className="py-1.5 pr-4 font-medium">Value</th>
            <th className="py-1.5 font-medium">From</th>
          </tr>
        </thead>
        <tbody>
          {config.map((c) => (
            <tr key={c.key} className="border-b align-top last:border-b-0">
              <td className="py-1.5 pr-4 font-mono text-xs whitespace-nowrap">{c.key}</td>
              <td className="py-1.5 pr-4 font-mono text-xs break-all whitespace-pre-line">
                {c.value || <span className="text-muted-foreground">—</span>}
              </td>
              <td className="py-1.5">
                <Badge variant={c.source === 'remote' ? 'secondary' : 'outline'} className={c.source === 'default' ? 'text-muted-foreground' : undefined}>
                  {sourceLabels[c.source]}
                </Badge>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export function MachineSettings({ details, onSaved }: { details: IndexerDetails; onSaved: () => void }) {
  const [state, setState] = useState<FormState>(() => initialState(details));
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [message, setMessage] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const status = details.status;
  const settings = details.settings;
  const remote = status?.remoteManagement ?? false;
  const applying = settings !== null && status !== null && status.appliedRevision !== settings.revision;

  const save = async (next: FormState) => {
    setSaving(true);
    setErrors({});
    setMessage(null);
    try {
      await saveIndexerSettings(details.machineId, toSettings(next));
      setMessage('Saved. The indexer restarts with these settings within a few seconds, and rescans.');
      onSaved();
    } catch (e) {
      if (e instanceof ApiError && Object.keys(e.fieldErrors).length > 0) setErrors(fieldMessages(e.fieldErrors));
      else setMessage((e as Error).message);
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="space-y-8 text-sm">
      <section>
        <Heading>Change from here</Heading>
        {!remote ? (
          <p className="text-muted-foreground">
            This machine doesn't allow remote management. To change its settings here, set{' '}
            <code className="font-mono text-xs">Indexer:RemoteManagement</code> to <code className="font-mono text-xs">true</code> in
            its own <code className="font-mono text-xs">appsettings.Local.json</code> and restart the service.
          </p>
        ) : (
          <form
            className="space-y-4"
            onSubmit={(e) => {
              e.preventDefault();
              void save(state);
            }}
          >
            <p className="text-muted-foreground">
              Tick a setting to override the machine's own value. Saving restarts the indexer, which then scans every watch path again.
            </p>
            {fields.map((f) => (
              <SettingRow
                key={f.key}
                field={f}
                state={state[f.key]}
                error={errors[f.key]}
                onChange={(next) => setState((s) => ({ ...s, [f.key]: next }))}
              />
            ))}
            <div className="flex flex-wrap items-center gap-3">
              <Button type="submit" size="sm" disabled={saving}>Save settings</Button>
              {settings && Object.values(settings.settings).some((v) => v !== null) && (
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  disabled={saving}
                  onClick={() => {
                    const cleared = Object.fromEntries(Object.entries(state).map(([k, v]) => [k, { ...v, set: false }]));
                    setState(cleared);
                    void save(cleared);
                  }}
                >
                  Use the machine's own settings
                </Button>
              )}
              {message && <p role="status" className="text-sm">{message}</p>}
            </div>
            {settings && (
              <p className="text-xs text-muted-foreground">
                {applying
                  ? `Waiting for the indexer to apply revision ${settings.revision}${status?.online ? '…' : '; it is offline and applies it when it is back.'}`
                  : `Revision ${settings.revision} is in effect.`}{' '}
                Last changed by {settings.updatedBy}, {formatDateTime(settings.updatedAt)}.
              </p>
            )}
          </form>
        )}
      </section>

      {status?.config && (
        <section>
          <Heading>In effect</Heading>
          <InEffect config={status.config} />
          <p className="mt-2 text-xs text-muted-foreground">
            As last reported. Passwords and the license key are never sent; settings that start programs or load data files can only be changed on the machine.
          </p>
        </section>
      )}
    </div>
  );
}
