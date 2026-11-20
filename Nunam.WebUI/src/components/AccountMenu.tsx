import { useState } from 'react';
import { KeyRound, LogOut, MonitorSmartphone, Users, UserRound } from 'lucide-react';
import { useAuth } from '@/auth/context';
import { ChangePasswordForm } from '@/auth/AuthScreens';
import { SessionsDialog } from '@/components/SessionsDialog';
import { UsersDialog } from '@/components/UsersDialog';
import { Button } from '@/components/ui/button';
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';

type Open = 'password' | 'sessions' | 'users' | null;

export function AccountMenu() {
  const { user, setUser, signOut } = useAuth();
  const [open, setOpen] = useState<Open>(null);
  const close = (isOpen: boolean) => !isOpen && setOpen(null);

  return (
    <>
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button variant="ghost" size="sm" aria-label={`Account: ${user.displayName}`}>
            <UserRound />
            <span className="hidden max-w-32 truncate sm:inline">{user.displayName}</span>
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end" className="w-56">
          <DropdownMenuLabel className="font-normal">
            <div className="truncate font-medium">{user.displayName}</div>
            <div className="truncate text-xs text-muted-foreground">
              {user.userName} · {user.role === 'admin' ? 'admin' : 'member'}
            </div>
          </DropdownMenuLabel>
          <DropdownMenuSeparator />
          <DropdownMenuItem onSelect={() => setOpen('password')}><KeyRound />Change password</DropdownMenuItem>
          <DropdownMenuItem onSelect={() => setOpen('sessions')}><MonitorSmartphone />Signed-in devices</DropdownMenuItem>
          {user.role === 'admin' && (
            <DropdownMenuItem onSelect={() => setOpen('users')}><Users />People</DropdownMenuItem>
          )}
          <DropdownMenuSeparator />
          <DropdownMenuItem onSelect={() => void signOut()}><LogOut />Sign out</DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>

      <Dialog open={open === 'password'} onOpenChange={close}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>Change password</DialogTitle>
            <DialogDescription>Other devices signed in to your account are signed out.</DialogDescription>
          </DialogHeader>
          <ChangePasswordForm
            onDone={(updated) => {
              setUser(updated);
              setOpen(null);
            }}
          />
        </DialogContent>
      </Dialog>

      <SessionsDialog open={open === 'sessions'} onOpenChange={close} />
      {user.role === 'admin' && <UsersDialog open={open === 'users'} onOpenChange={close} />}
    </>
  );
}
