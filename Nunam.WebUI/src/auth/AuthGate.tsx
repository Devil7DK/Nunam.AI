import { useCallback, useEffect, useMemo, useState } from 'react';
import { getAuthState, signOut as apiSignOut, UNAUTHORIZED_EVENT, type AuthState, type UserInfo } from '@/api/nunam';
import { AuthContext, type Auth } from '@/auth/context';
import { ChangePasswordScreen, SetupScreen, SignInScreen } from '@/auth/AuthScreens';
import { Button } from '@/components/ui/button';

/** Shows setup, sign-in or the forced password change until there is a usable session, then the app. */
export function AuthGate({ children }: { children: React.ReactNode }) {
  const [state, setState] = useState<AuthState | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(() => {
    getAuthState()
      .then((s) => {
        setState(s);
        setError(null);
      })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)));
  }, []);

  useEffect(() => {
    load();
    const expired = () => setState((s) => (s ? { ...s, user: null } : s));
    window.addEventListener(UNAUTHORIZED_EVENT, expired);
    return () => window.removeEventListener(UNAUTHORIZED_EVENT, expired);
  }, [load]);

  const setUser = useCallback((user: UserInfo) => setState({ setupRequired: false, user }), []);
  const signOut = useCallback(async (everywhere = false) => {
    try {
      await apiSignOut(everywhere);
    } finally {
      setState({ setupRequired: false, user: null });
    }
  }, []);

  const user = state?.user;
  const auth = useMemo<Auth | null>(() => (user ? { user, setUser, signOut } : null), [user, setUser, signOut]);

  if (error) {
    return (
      <div className="mx-auto max-w-6xl px-4 py-10 text-sm">
        <p className="text-destructive">Can't reach the Nunam API: {error}</p>
        <Button variant="outline" size="sm" className="mt-3" onClick={load}>Try again</Button>
      </div>
    );
  }
  if (!state) return null;
  if (state.setupRequired) return <SetupScreen onDone={setUser} />;
  if (!auth) return <SignInScreen onDone={setUser} />;
  if (auth.user.mustChangePassword) {
    return <ChangePasswordScreen user={auth.user} onDone={setUser} onSignOut={() => void signOut()} />;
  }

  return <AuthContext.Provider value={auth}>{children}</AuthContext.Provider>;
}
