import { useState } from 'react';
import {
  ApiError,
  createDescribeProvider,
  updateDescribeProvider,
  type AIProviderKind,
  type BudgetPeriod,
  type DescribeProvider,
  type DescribeProviderInput,
} from '@/api/nunam';
import { Field } from '@/components/Field';
import { Button } from '@/components/ui/button';
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { Label } from '@/components/ui/label';

interface Preset {
  label: string;
  values: Partial<DescribeProviderInput>;
  shortPrompt?: boolean;
  keyHint?: string;
}

// Prices are list prices per million tokens as of November 2026; check the provider's page.
const presets: Preset[] = [
  {
    label: 'Ollama on this network',
    values: { name: 'Local Ollama', kind: 'ollama', baseUrl: 'http://ollama-host:11434', model: 'gemma4:12b-it-qat', concurrency: 1, inputPricePerMillion: 0, outputPricePerMillion: 0 },
  },
  {
    label: 'Anthropic (Claude)',
    values: { name: 'Claude Haiku', kind: 'openAICompatible', baseUrl: 'https://api.anthropic.com/v1', model: 'claude-haiku-5-5', concurrency: 4, inputPricePerMillion: 0.1, outputPricePerMillion: 0.5 },
    shortPrompt: true,
    keyHint: 'From console.anthropic.com, Settings, API keys.',
  },
  {
    label: 'OpenRouter',
    values: { name: 'OpenRouter', kind: 'openAICompatible', baseUrl: 'https://openrouter.ai/api/v1', model: 'google/gemma-4-26b-a4b-it', concurrency: 4, inputPricePerMillion: 0, outputPricePerMillion: 0 },
    shortPrompt: true,
    keyHint: 'From openrouter.ai, Keys. Enter the model’s prices from its page on openrouter.ai.',
  },
  {
    label: 'Google Gemini',
    // Gemini's OpenAI-compatible endpoint refuses the parameter that turns reasoning off.
    values: { name: 'Gemini Flash-Lite', kind: 'openAICompatible', baseUrl: 'https://generativelanguage.googleapis.com/v1beta/openai', model: 'gemini-3.5-flash-lite', concurrency: 4, disableReasoning: false, inputPricePerMillion: 0.3, outputPricePerMillion: 2.5 },
    shortPrompt: true,
    keyHint: 'From aistudio.google.com, Get API key.',
  },
  { label: 'Other OpenAI-compatible server', values: { name: '', kind: 'openAICompatible', baseUrl: '', model: '' } },
];

const blank: DescribeProviderInput = {
  name: '', kind: 'ollama', baseUrl: '', model: '', prompt: null, maxOutputTokens: 1024, disableReasoning: true,
  priority: 1, concurrency: 1, takesOverflow: false, enabled: true, inputPricePerMillion: 0, outputPricePerMillion: 0,
  budgetUsd: null, budgetPeriod: 'monthly',
};

function toInput(p: DescribeProvider): DescribeProviderInput {
  return {
    name: p.name, kind: p.kind, baseUrl: p.baseUrl, model: p.model, prompt: p.prompt, maxOutputTokens: p.maxOutputTokens,
    disableReasoning: p.disableReasoning, priority: p.priority, concurrency: p.concurrency, takesOverflow: p.takesOverflow,
    enabled: p.enabled, inputPricePerMillion: p.inputPricePerMillion, outputPricePerMillion: p.outputPricePerMillion,
    budgetUsd: p.budgetUsd, budgetPeriod: p.budgetPeriod,
  };
}

const inputClass =
  'w-full rounded-md border bg-transparent px-3 py-2 text-sm shadow-xs outline-none focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50 aria-invalid:border-destructive';

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Editing this provider; adding a new one when null. */
  provider: DescribeProvider | null;
  defaultPrompt: string;
  shortPrompt: string;
  secretsConfigured: boolean;
  nextPriority: number;
  onSaved: () => void;
}

export function ProviderDialog(props: Props) {
  return (
    <Dialog open={props.open} onOpenChange={props.onOpenChange}>
      <DialogContent className="flex max-h-[90vh] flex-col gap-0 overflow-hidden p-0 sm:max-w-2xl">
        <DialogHeader className="border-b px-6 py-4">
          <DialogTitle>{props.provider ? `Edit ${props.provider.name}` : 'Add a describe provider'}</DialogTitle>
          <DialogDescription>A vision model that writes the descriptions images and scanned pages are searched by.</DialogDescription>
        </DialogHeader>
        {/* Mounted afresh each time the dialog opens, so it starts from the provider as saved. */}
        <ProviderForm {...props} />
      </DialogContent>
    </Dialog>
  );
}

function ProviderForm({ onOpenChange, provider, defaultPrompt, shortPrompt, secretsConfigured, nextPriority, onSaved }: Props) {
  const [form, setForm] = useState<DescribeProviderInput>(() => (provider ? toInput(provider) : { ...blank, priority: nextPriority }));
  const [apiKey, setApiKey] = useState('');
  const [removeKey, setRemoveKey] = useState(false);
  const [budget, setBudget] = useState(provider?.budgetUsd != null ? String(provider.budgetUsd) : '');
  const [keyHint, setKeyHint] = useState<string | undefined>();
  const [errors, setErrors] = useState<Record<string, string[]>>({});
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const set = (patch: Partial<DescribeProviderInput>) => setForm((f) => ({ ...f, ...patch }));
  const fieldError = (key: string) => errors[key]?.join(' ');

  const applyPreset = (preset: Preset) => {
    set({ ...blank, priority: form.priority, ...preset.values, prompt: preset.shortPrompt ? shortPrompt : null });
    setKeyHint(preset.keyHint);
  };

  const save = async (e: React.FormEvent) => {
    e.preventDefault();
    setSaving(true);
    setErrors({});
    setError(null);
    const body: DescribeProviderInput = {
      ...form,
      prompt: form.prompt?.trim() ? form.prompt : null,
      budgetUsd: budget.trim() === '' ? null : Number(budget),
      apiKey: removeKey ? '' : apiKey.trim() ? apiKey.trim() : provider ? undefined : '',
    };
    try {
      if (provider) await updateDescribeProvider(provider.id, body);
      else await createDescribeProvider(body);
      onSaved();
      onOpenChange(false);
    } catch (err) {
      if (err instanceof ApiError) {
        setErrors(err.fieldErrors);
        if (Object.keys(err.fieldErrors).length === 0) setError(err.message);
      } else {
        setError('Could not save the provider.');
      }
    } finally {
      setSaving(false);
    }
  };

  const number = (value: string) => (value === '' ? 0 : Number(value));

  return (
    <form onSubmit={save} className="grid gap-5 overflow-y-auto px-6 py-5">
      {!provider && (
        <div className="grid gap-1.5">
          <Label>Start from</Label>
          <div className="flex flex-wrap gap-1.5">
            {presets.map((p) => (
              <Button key={p.label} type="button" variant="outline" size="sm" onClick={() => applyPreset(p)}>{p.label}</Button>
            ))}
          </div>
        </div>
      )}

      <div className="grid gap-4 sm:grid-cols-2">
        <Field label="Name" value={form.name} onChange={(e) => set({ name: e.target.value })} error={fieldError('name')} maxLength={100} required />
        <div className="grid gap-1.5">
          <Label htmlFor="provider-kind">API</Label>
          <select id="provider-kind" className={`${inputClass} h-9 py-0`} value={form.kind} onChange={(e) => set({ kind: e.target.value as AIProviderKind })}>
            <option value="ollama">Ollama</option>
            <option value="openAICompatible">OpenAI-compatible (Anthropic, Gemini, OpenRouter, LM Studio)</option>
          </select>
        </div>
        <Field label="Address" value={form.baseUrl} onChange={(e) => set({ baseUrl: e.target.value })} error={fieldError('baseUrl')} placeholder="https://api.example.com/v1" required />
        <Field label="Model" value={form.model} onChange={(e) => set({ model: e.target.value })} error={fieldError('model')} spellCheck={false} required />
      </div>

      {form.kind === 'openAICompatible' && (
        <div className="grid gap-1.5">
          <Field
            label="API key"
            type="password"
            autoComplete="off"
            value={apiKey}
            disabled={removeKey || !secretsConfigured}
            onChange={(e) => setApiKey(e.target.value)}
            placeholder={provider?.hasApiKey ? 'Stored. Enter a new one to replace it.' : ''}
            error={fieldError('apiKey')}
            hint={!secretsConfigured ? 'The server has no secrets key, so it can’t store API keys. Set NUNAM_SECRETS_KEY in its .env and restart it.' : keyHint ?? 'Stored encrypted; only the AI worker receives it.'}
          />
          {provider?.hasApiKey && (
            <label className="flex items-center gap-2 text-sm">
              <input type="checkbox" className="size-4 accent-foreground" checked={removeKey} onChange={(e) => setRemoveKey(e.target.checked)} />
              Remove the stored key
            </label>
          )}
        </div>
      )}

      <div className="grid gap-1.5">
        <div className="flex items-center justify-between gap-2">
          <Label htmlFor="provider-prompt">Prompt</Label>
          <span className="flex gap-1">
            <Button type="button" variant="ghost" size="sm" className="h-7" onClick={() => set({ prompt: null })}>Default</Button>
            <Button type="button" variant="ghost" size="sm" className="h-7" onClick={() => set({ prompt: shortPrompt })}>Short</Button>
          </span>
        </div>
        <textarea
          id="provider-prompt"
          rows={3}
          className={inputClass}
          value={form.prompt ?? ''}
          placeholder={defaultPrompt}
          aria-invalid={fieldError('prompt') ? true : undefined}
          onChange={(e) => set({ prompt: e.target.value })}
        />
        <p className="text-xs text-muted-foreground">
          Empty uses the default. The short prompt asks for one paragraph of up to 80 words, which costs about a third of the default’s output on a paid model.
        </p>
      </div>

      <div className="grid gap-4 sm:grid-cols-3">
        <Field label="Priority" type="number" min={1} max={100} value={form.priority} onChange={(e) => set({ priority: number(e.target.value) })} error={fieldError('priority')} hint="1 goes first; equal numbers share the work." />
        <Field label="At a time" type="number" min={1} max={32} value={form.concurrency} onChange={(e) => set({ concurrency: number(e.target.value) })} error={fieldError('concurrency')} hint="Images described in parallel." />
        <Field label="Longest answer (tokens)" type="number" min={16} max={16384} value={form.maxOutputTokens} onChange={(e) => set({ maxOutputTokens: number(e.target.value) })} error={fieldError('maxOutputTokens')} />
      </div>

      <div className="grid gap-2">
        <label className="flex items-start gap-2.5 text-sm">
          <input type="checkbox" className="mt-0.5 size-4 accent-foreground" checked={form.takesOverflow} onChange={(e) => set({ takesOverflow: e.target.checked })} />
          <span>
            Take overflow
            <span className="block text-xs text-muted-foreground">
              Also describe while every higher-priority provider is busy. Off, it only steps in while they’re disabled, failing or over budget.
            </span>
          </span>
        </label>
        <label className="flex items-start gap-2.5 text-sm">
          <input type="checkbox" className="mt-0.5 size-4 accent-foreground" checked={form.disableReasoning} onChange={(e) => set({ disableReasoning: e.target.checked })} />
          <span>
            Turn off reasoning
            <span className="block text-xs text-muted-foreground">A description needs none, and reasoning tokens are billed. Untick it for a server that refuses the setting, such as Gemini.</span>
          </span>
        </label>
        <label className="flex items-center gap-2.5 text-sm">
          <input type="checkbox" className="size-4 accent-foreground" checked={form.enabled} onChange={(e) => set({ enabled: e.target.checked })} />
          Enabled
        </label>
      </div>

      <div className="grid gap-4 sm:grid-cols-2">
        <Field label="Input price (US$ per million tokens)" type="number" min={0} step="any" value={form.inputPricePerMillion} onChange={(e) => set({ inputPricePerMillion: number(e.target.value) })} error={fieldError('inputPricePerMillion')} />
        <Field label="Output price (US$ per million tokens)" type="number" min={0} step="any" value={form.outputPricePerMillion} onChange={(e) => set({ outputPricePerMillion: number(e.target.value) })} error={fieldError('outputPricePerMillion')} />
        <Field label="Budget (US$)" type="number" min={0} step="any" value={budget} onChange={(e) => setBudget(e.target.value)} error={fieldError('budgetUsd')} hint="Stops the provider once it has spent this much. Empty for no limit." />
        <div className="grid gap-1.5 content-start">
          <Label htmlFor="provider-period">Budget counts</Label>
          <select id="provider-period" className={`${inputClass} h-9 py-0`} value={form.budgetPeriod} onChange={(e) => set({ budgetPeriod: e.target.value as BudgetPeriod })}>
            <option value="monthly">Each calendar month</option>
            <option value="total">Everything spent so far</option>
          </select>
        </div>
      </div>

      {error && <p role="alert" className="text-sm text-destructive">{error}</p>}
      <div className="flex justify-end gap-2 border-t pt-4">
        <Button type="button" variant="ghost" onClick={() => onOpenChange(false)}>Cancel</Button>
        <Button type="submit" disabled={saving}>{saving ? 'Saving…' : provider ? 'Save' : 'Add provider'}</Button>
      </div>
    </form>
  );
}
