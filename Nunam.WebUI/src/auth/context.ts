import { createContext, useContext } from 'react';
import type { UserInfo } from '@/api/nunam';

export interface Auth {
  user: UserInfo;
  /** Replaces the signed-in user, e.g. after a password change clears the must-change flag. */
  setUser: (user: UserInfo) => void;
  signOut: (everywhere?: boolean) => Promise<void>;
}

export const AuthContext = createContext<Auth | null>(null);

export function useAuth(): Auth {
  const auth = useContext(AuthContext);
  if (!auth) throw new Error('useAuth needs an AuthGate above it.');
  return auth;
}
