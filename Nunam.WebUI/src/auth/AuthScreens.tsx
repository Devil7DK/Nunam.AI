import { useState } from 'react';
import { ApiError, changePassword, setup, signIn, type UserInfo } from '@/api/nunam';
import { Field } from '@/components/Field';
import { fieldMessages } from '@/lib/forms';
import { Button } from '@/components/ui/button';

/** Runs a form submission, turning API errors into a form message and per-field messages. */
function useSubmit<T>(action: () => Promise<T>, onDone: (result: T) => void) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [fields, setFields] = useState<Record<string, string>>({});

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError(null);
    setFields({});
    try {
      onDone(await action());
    } catch (e) {
      if (e instanceof ApiError && Object.keys(e.fieldErrors).length > 0) setFields(fieldMessages(e.fieldErrors));
      else setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  };

  return { busy, error, fields, submit };
}

function Screen({ title, intro, children }: { title: string; intro: React.ReactNode; children: React.ReactNode }) {
  return (
    <div className="min-h-svh bg-background text-foreground">
      <header className="border-b">
        <div className="mx-auto max-w-6xl px-4 py-3">
          <span className="text-lg font-semibold tracking-tight">Nunam</span>
        </div>
      </header>
      <main className="mx-auto max-w-6xl px-4 py-10">
        <div className="max-w-sm">
          <h1 className="text-xl font-semibold tracking-tight">{title}</h1>
          <div className="mt-1 mb-6 text-sm text-muted-foreground">{intro}</div>
          {children}
        </div>
      </main>
    </div>
  );
}

function FormError({ message }: { message: string | null }) {
  return message ? <p role="alert" className="text-sm text-destructive">{message}</p> : null;
}

export function SetupScreen({ onDone }: { onDone: (user: UserInfo) => void }) {
  const [code, setCode] = useState('');
  const [userName, setUserName] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [password, setPassword] = useState('');
  const form = useSubmit(() => setup({ code, userName, displayName, password }), onDone);

  return (
    <Screen
      title="Set up Nunam"
      intro={<>Create the first admin account. The setup code is in the API's log (<code className="font-mono">docker compose logs nunam-api</code>).</>}
    >
      <form className="grid gap-4" onSubmit={form.submit}>
        <Field label="Setup code" value={code} onChange={(e) => setCode(e.target.value)} error={form.fields.code}
          autoComplete="off" spellCheck={false} required className="font-mono uppercase" />
        <Field label="User name" value={userName} onChange={(e) => setUserName(e.target.value)} error={form.fields.userName}
          autoComplete="username" autoCapitalize="none" spellCheck={false} required />
        <Field label="Display name" value={displayName} onChange={(e) => setDisplayName(e.target.value)}
          error={form.fields.displayName} hint="Optional. Shown to people you share machines with." autoComplete="name" />
        <Field label="Password" type="password" value={password} onChange={(e) => setPassword(e.target.value)}
          error={form.fields.password} hint="At least 10 characters." autoComplete="new-password" required />
        <FormError message={form.error} />
        <Button type="submit" disabled={form.busy} className="justify-self-start">Create admin</Button>
      </form>
    </Screen>
  );
}

export function SignInScreen({ onDone }: { onDone: (user: UserInfo) => void }) {
  const [userName, setUserName] = useState('');
  const [password, setPassword] = useState('');
  const form = useSubmit(() => signIn({ userName, password }), onDone);

  return (
    <Screen title="Sign in" intro="Ask an admin for an account if you don't have one.">
      <form className="grid gap-4" onSubmit={form.submit}>
        <Field label="User name" value={userName} onChange={(e) => setUserName(e.target.value)}
          autoComplete="username" autoCapitalize="none" spellCheck={false} autoFocus required />
        <Field label="Password" type="password" value={password} onChange={(e) => setPassword(e.target.value)}
          autoComplete="current-password" required />
        <FormError message={form.error} />
        <Button type="submit" disabled={form.busy} className="justify-self-start">Sign in</Button>
      </form>
    </Screen>
  );
}

export function ChangePasswordForm({ onDone, submitLabel = 'Change password' }: {
  onDone: (user: UserInfo) => void;
  submitLabel?: string;
}) {
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const mismatch = confirm !== '' && confirm !== newPassword;
  const form = useSubmit(() => changePassword({ currentPassword, newPassword }), onDone);

  return (
    <form className="grid gap-4" onSubmit={(e) => (mismatch ? e.preventDefault() : form.submit(e))}>
      <Field label="Current password" type="password" value={currentPassword} onChange={(e) => setCurrentPassword(e.target.value)}
        error={form.fields.currentPassword} autoComplete="current-password" required />
      <Field label="New password" type="password" value={newPassword} onChange={(e) => setNewPassword(e.target.value)}
        error={form.fields.newPassword} hint="At least 10 characters." autoComplete="new-password" required />
      <Field label="Repeat new password" type="password" value={confirm} onChange={(e) => setConfirm(e.target.value)}
        error={mismatch ? "Doesn't match the new password." : undefined} autoComplete="new-password" required />
      <FormError message={form.error} />
      <Button type="submit" disabled={form.busy || mismatch} className="justify-self-start">{submitLabel}</Button>
    </form>
  );
}

export function ChangePasswordScreen({ user, onDone, onSignOut }: {
  user: UserInfo;
  onDone: (user: UserInfo) => void;
  onSignOut: () => void;
}) {
  return (
    <Screen
      title="Choose a new password"
      intro={<>Signed in as <span className="font-medium text-foreground">{user.userName}</span> with a temporary password. Pick your own to continue.</>}
    >
      <ChangePasswordForm onDone={onDone} submitLabel="Save and continue" />
      <Button variant="link" className="mt-4 px-0" onClick={onSignOut}>Sign out instead</Button>
    </Screen>
  );
}
