import { FileAudio, FileImage, FileQuestion, FileText, FileVideo, type LucideProps } from 'lucide-react';
import type { FileType } from '@/api/nunam';

const icons = {
  document: FileText,
  image: FileImage,
  audio: FileAudio,
  video: FileVideo,
  other: FileQuestion,
} satisfies Record<FileType, unknown>;

export function FileIcon({ type, ...props }: { type: FileType } & LucideProps) {
  const Icon = icons[type];
  return <Icon aria-hidden {...props} />;
}
