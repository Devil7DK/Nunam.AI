import { Marked, type Tokens } from 'marked';

import { GITHUB_URL } from './Constants';

const sources = import.meta.glob<string>('../../docs/*.md', {
  query: '?raw',
  import: 'default',
  eager: true,
});

export interface Doc {
  slug: string;
  title: string;
  summary: string;
  html: string;
  headings: { id: string; text: string }[];
}

const ORDER = ['overview', 'deployment', 'adding-a-machine', 'extending-the-indexer'];

const SUMMARIES: Record<string, string> = {
  overview: 'What runs where, and what leaves each machine.',
  deployment: 'Postgres, the API, the web UI and the AI worker in Docker.',
  'adding-a-machine':
    'Install the indexer on Windows or Linux and point it at your folders.',
  'extending-the-indexer':
    'Choose parsers per file type, or add your own with commands and plugins.',
};

const slugify = (text: string): string =>
  text
    .toLowerCase()
    .replace(/<[^>]+>/g, '')
    .replace(/[^\p{L}\p{N}\s-]/gu, '')
    .trim()
    .replace(/\s+/g, '-');

// Links between docs point at the .md files so they also work on GitHub.
const rewriteHref = (href: string): string => {
  const match = /^([\w-]+)\.md(#.*)?$/.exec(href);
  if (match) return `/docs/${match[1]}${match[2] ?? ''}`;
  if (/^(https?:|mailto:|#|\/)/.test(href)) return href;
  return `${GITHUB_URL}/blob/master/docs/${href}`;
};

const render = (slug: string, markdown: string): Doc => {
  const headings: Doc['headings'] = [];
  let title = slug;

  const marked = new Marked({
    gfm: true,
    walkTokens(token) {
      if (token.type === 'link') {
        const link = token as Tokens.Link;
        link.href = rewriteHref(link.href);
      }
    },
    renderer: {
      heading({ tokens, depth, text }) {
        const inner = this.parser.parseInline(tokens);
        if (depth === 1) {
          title = text;
          return '';
        }
        const id = slugify(text);
        if (depth === 2) headings.push({ id, text });
        return `<h${depth} id="${id}">${inner}</h${depth}>\n`;
      },
      link({ href, title: linkTitle, tokens }) {
        const inner = this.parser.parseInline(tokens);
        const external = /^https?:/.test(href);
        const attrs = external ? ' target="_blank" rel="noreferrer"' : '';
        const t = linkTitle ? ` title="${linkTitle}"` : '';
        return `<a href="${href}"${t}${attrs}>${inner}</a>`;
      },
    },
  });

  const html = marked.parse(markdown, { async: false });
  return { slug, title, summary: SUMMARIES[slug] ?? '', html, headings };
};

export const DOCS: Doc[] = Object.entries(sources)
  .map(([path, markdown]) => render(path.replace(/^.*\/|\.md$/g, ''), markdown))
  .sort((a, b) => {
    const rank = (d: Doc) => {
      const i = ORDER.indexOf(d.slug);
      return i === -1 ? ORDER.length : i;
    };
    return rank(a) - rank(b) || a.title.localeCompare(b.title);
  });

export const findDoc = (slug: string | undefined): Doc | undefined =>
  DOCS.find((d) => d.slug === (slug ?? DOCS[0]?.slug));

export const docTitle = (pathname: string): string | null => {
  const match = /^\/docs(?:\/([\w-]+))?\/?$/.exec(pathname);
  if (!match) return null;
  return findDoc(match[1])?.title ?? null;
};
