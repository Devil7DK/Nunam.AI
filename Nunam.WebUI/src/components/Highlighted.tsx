import { MATCH_END, MATCH_START } from '@/api/nunam';

/** Renders a search snippet as text, with the API's U+E000…U+E001 marked words in <mark>. */
export function Highlighted({ text, className }: { text: string; className?: string }) {
  const parts = text.split(MATCH_START);
  return (
    <span className={className}>
      {parts[0]}
      {parts.slice(1).map((part, i) => {
        const [match, rest = ''] = part.split(MATCH_END);
        return (
          <span key={i}>
            <mark className="rounded-[2px] bg-amber-200/70 px-px text-inherit dark:bg-amber-400/25">{match}</mark>
            {rest}
          </span>
        );
      })}
    </span>
  );
}
