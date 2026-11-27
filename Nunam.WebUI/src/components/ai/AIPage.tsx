import { useCallback, useEffect, useState } from 'react';
import { FlaskConical, Pencil, Plus, Trash2 } from 'lucide-react';
import {
  deleteDescribeProvider,
  getAISettings,
  testDescribeProvider,
  updateDescribeProvider,
  type AISettings,
  type DescribeProvider,
  type DescribeTestResult,
} from '@/api/nunam';
import { ProviderDialog } from '@/components/ai/ProviderDialog';
import { Button } from '@/components/ui/button';
import { formatRelative } from '@/lib/format';

const REFRESH_MS = 15_000;

const money = (value: number) =>
  value === 0 ? '$0' : value < 0.01 ? `$${value.toFixed(4)}` : `$${value.toFixed(2)}`;

const statusLook: Record<DescribeProvider['status'], { label: string; dot: string }> = {
  ok: { label: 'Ready', dot: 'bg-emerald-500' },
  disabled: { label: 'Disabled', dot: 'bg-muted-foreground/40' },
  budget: { label: 'Budget spent', dot: 'bg-amber-500' },
  error: { label: 'Failing', dot: 'bg-destructive' },
};

function host(url: string) {
  try {
    return new URL(url).host;
  } catch {
    return url;
  }
}

function Section({ title, action, children }: { title: string; action?: React.ReactNode; children: React.ReactNode }) {
  return (
    <section className="space-y-3">
      <div className="flex items-center justify-between gap-3">
        <h2 className="text-sm font-semibold">{title}</h2>
        {action}
      </div>
      {children}
    </section>
  );
}

function ProviderRow({ provider, settings, onEdit, onChanged }: {
  provider: DescribeProvider;
  settings: AISettings;
  onEdit: () => void;
  onChanged: () => void;
}) {
  const [testing, setTesting] = useState(false);
  const [test, setTest] = useState<DescribeTestResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const look = statusLook[provider.status];
  const period = provider.budgetPeriod === 'monthly' ? 'this month' : 'in total';

  const runTest = async () => {
    setTesting(true);
    setTest(null);
    setError(null);
    try {
      setTest(await testDescribeProvider(provider.id));
      onChanged();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'The test failed.');
    } finally {
      setTesting(false);
    }
  };

  const toggle = async () => {
    setError(null);
    try {
      await updateDescribeProvider(provider.id, {
        ...provider, enabled: !provider.enabled, apiKey: undefined,
      });
      onChanged();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not change the provider.');
    }
  };

  const remove = async () => {
    if (!window.confirm(`Remove ${provider.name}? Its spending history goes with it; descriptions it wrote stay.`)) return;
    try {
      await deleteDescribeProvider(provider.id);
      onChanged();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not remove the provider.');
    }
  };

  return (
    <li className="space-y-2 py-4 first:pt-0 last:pb-0">
      <div className="flex flex-wrap items-start gap-x-4 gap-y-2">
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <span className={`inline-block size-2 shrink-0 rounded-full ${look.dot}`} aria-hidden />
            <span className="font-medium">{provider.name}</span>
            <span className="text-xs text-muted-foreground">{look.label}</span>
          </div>
          <p className="mt-0.5 truncate font-mono text-xs text-muted-foreground">
            {provider.model} · {host(provider.baseUrl)}
          </p>
          <p className="mt-1 text-sm text-muted-foreground">
            Priority {provider.priority} · {provider.concurrency} at a time
            {provider.priority > Math.min(...settings.providers.map((p) => p.priority)) &&
              (provider.takesOverflow ? ' · backup and overflow' : ' · backup only')}
            {provider.prompt === settings.shortPrompt && ' · short prompt'}
          </p>
          <p className="text-sm text-muted-foreground">
            {provider.inputPricePerMillion === 0 && provider.outputPricePerMillion === 0
              ? 'Free'
              : `${money(provider.spentThisPeriod)} spent ${period}`}
            {provider.budgetUsd != null && ` of ${money(provider.budgetUsd)}`}
            {provider.spentTotal > provider.spentThisPeriod && ` · ${money(provider.spentTotal)} in total`}
            {' · '}
            {provider.requestsToday.toLocaleString()} today
            {provider.failuresToday > 0 && <span className="text-destructive"> ({provider.failuresToday.toLocaleString()} failed)</span>}
            {provider.lastUsedAt && ` · last used ${formatRelative(provider.lastUsedAt)}`}
          </p>
          {provider.status === 'error' && provider.lastError && (
            <p className="mt-1 line-clamp-2 text-sm text-destructive" title={provider.lastError}>
              {formatRelative(provider.lastErrorAt)}: {provider.lastError}
            </p>
          )}
        </div>
        <div className="flex shrink-0 gap-1">
          <Button variant="outline" size="sm" onClick={runTest} disabled={testing}>
            <FlaskConical />
            {testing ? 'Testing…' : 'Test'}
          </Button>
          <Button variant="ghost" size="sm" onClick={toggle}>{provider.enabled ? 'Disable' : 'Enable'}</Button>
          <Button variant="ghost" size="icon" aria-label={`Edit ${provider.name}`} onClick={onEdit}><Pencil /></Button>
          <Button variant="ghost" size="icon" aria-label={`Remove ${provider.name}`} onClick={remove}><Trash2 /></Button>
        </div>
      </div>

      {error && <p role="alert" className="text-sm text-destructive">{error}</p>}
      {test && (
        <div className="rounded-md border bg-muted/40 px-3 py-2 text-sm">
          <p className="truncate font-mono text-xs text-muted-foreground" title={test.path}>{test.fileName}</p>
          {test.text ? <p className="mt-1 leading-relaxed">{test.text}</p> : <p className="mt-1 text-destructive">{test.error}</p>}
          <p className="mt-1 text-xs text-muted-foreground">
            {test.inputTokens.toLocaleString()} tokens in, {test.outputTokens.toLocaleString()} out · {money(test.costUsd)} · {test.seconds.toFixed(1)} s
          </p>
        </div>
      )}
    </li>
  );
}

/** Admins' view of the AI models: who describes images, at what cost, and the embedding model behind search. */
export function AIPage({ onNavigate }: { onNavigate: (path: string) => void }) {
  const [settings, setSettings] = useState<AISettings | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState<DescribeProvider | null>(null);
  const [dialogOpen, setDialogOpen] = useState(false);

  const load = useCallback((signal?: AbortSignal) => {
    getAISettings(signal).then(
      (s) => { setSettings(s); setError(null); },
      (e: Error) => { if (e.name !== 'AbortError') setError(e.message); },
    );
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    load(controller.signal);
    const timer = setInterval(() => load(controller.signal), REFRESH_MS);
    return () => {
      controller.abort();
      clearInterval(timer);
    };
  }, [load]);

  const openDialog = (provider: DescribeProvider | null) => {
    setEditing(provider);
    setDialogOpen(true);
  };

  if (!settings) {
    return (
      <main className="mx-auto max-w-4xl px-4 py-6 text-sm text-muted-foreground">
        {error ?? 'Loading…'}
      </main>
    );
  }

  const { queue } = settings;
  const nextPriority = settings.providers.length ? Math.max(...settings.providers.map((p) => p.priority)) + 1 : 1;

  return (
    <main className="mx-auto max-w-4xl space-y-8 px-4 py-6">
      <div>
        <h1 className="text-lg font-semibold">AI</h1>
        <p className="text-sm text-muted-foreground">
          Vision models describe images and scanned pages so they can be searched by what they show; an embedding model turns text into the vectors semantic search compares.
        </p>
      </div>
      {error && <p role="alert" className="text-sm text-destructive">{error}</p>}

      <Section
        title="Description providers"
        action={<Button size="sm" onClick={() => openDialog(null)}><Plus />Add provider</Button>}
      >
        <p className="text-sm text-muted-foreground">
          {queue.pending.toLocaleString()} waiting · {queue.processing.toLocaleString()} in progress · {queue.done.toLocaleString()} described
          {queue.failed > 0 && ` · ${queue.failed.toLocaleString()} failed`}
        </p>
        {!settings.secretsConfigured && (
          <p className="rounded-md border px-3 py-2 text-sm">
            API keys can’t be stored yet: set <code className="font-mono text-xs">NUNAM_SECRETS_KEY</code> in the server’s .env
            (<code className="font-mono text-xs">openssl rand -base64 32</code>) and restart it.
          </p>
        )}
        {settings.providers.length === 0 ? (
          <p className="rounded-md border px-3 py-6 text-center text-sm text-muted-foreground">
            No providers: images wait undescribed until one is added.
          </p>
        ) : (
          <ul className="divide-y rounded-md border px-4 py-4">
            {settings.providers.map((p) => (
              <ProviderRow key={p.id} provider={p} settings={settings} onEdit={() => openDialog(p)} onChanged={() => load()} />
            ))}
          </ul>
        )}
      </Section>

      {settings.describedBy.length > 0 && (
        <Section
          title="Descriptions by model"
          action={<Button variant="ghost" size="sm" onClick={() => onNavigate('/?described=with')}>Browse described files</Button>}
        >
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b text-left text-xs text-muted-foreground">
                <th className="py-1.5 font-medium">Model</th>
                <th className="py-1.5 font-medium">Provider</th>
                <th className="py-1.5 text-right font-medium">Files</th>
              </tr>
            </thead>
            <tbody>
              {settings.describedBy.map((d) => (
                <tr key={`${d.model}|${d.provider}`} className="border-b last:border-b-0">
                  <td className="py-1.5 font-mono text-xs">{d.model ?? 'Unknown'}</td>
                  <td className="py-1.5">{d.provider ?? '—'}</td>
                  <td className="py-1.5 text-right tabular-nums">{d.files.toLocaleString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </Section>
      )}

      <Section title="Embeddings">
        <dl className="grid grid-cols-[8rem_1fr] gap-x-4 gap-y-1 text-sm">
          <dt className="text-muted-foreground">Model</dt>
          <dd className="font-mono text-xs leading-5">{settings.embedding.model} ({settings.embedding.dimensions} dimensions)</dd>
          <dt className="text-muted-foreground">Server</dt>
          <dd className="font-mono text-xs leading-5">{settings.embedding.baseUrl} ({settings.embedding.provider === 'ollama' ? 'Ollama' : 'OpenAI-compatible'})</dd>
        </dl>
        <p className="text-xs text-muted-foreground">
          Set in the server’s .env. Every vector has to come from the same model, so changing it means embedding every file again.
        </p>
      </Section>

      <ProviderDialog
        open={dialogOpen}
        onOpenChange={setDialogOpen}
        provider={editing}
        defaultPrompt={settings.defaultPrompt}
        shortPrompt={settings.shortPrompt}
        secretsConfigured={settings.secretsConfigured}
        nextPriority={nextPriority}
        onSaved={() => load()}
      />
    </main>
  );
}
