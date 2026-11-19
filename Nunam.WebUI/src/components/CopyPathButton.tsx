import { useState } from 'react';
import { Check, Copy } from 'lucide-react';
import { Button } from '@/components/ui/button';

/**
 * Browsers refuse to open file:// links from an http page, so the way to reach a file on its own
 * machine is to copy the path and paste it into that machine's file manager.
 */
export function CopyPathButton({ path, size = 'icon-sm' }: { path: string; size?: 'icon-sm' | 'sm' }) {
  const [copied, setCopied] = useState(false);

  const copy = async (event: React.MouseEvent) => {
    event.stopPropagation();
    try {
      await navigator.clipboard.writeText(path);
      setCopied(true);
      setTimeout(() => setCopied(false), 1500);
    } catch {
      // Clipboard needs a secure context (https or localhost); over plain LAN http it can be blocked.
      window.prompt('Copy the path:', path);
    }
  };

  return (
    <Button variant="ghost" size={size} onClick={copy} aria-label="Copy path" title="Copy path">
      {copied ? <Check /> : <Copy />}
      {size === 'sm' && (copied ? 'Copied' : 'Copy path')}
    </Button>
  );
}
