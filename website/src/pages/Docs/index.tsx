import './styles.css';

import type { FC, MouseEvent } from 'react';
import { Link, Navigate, useNavigate, useParams } from 'react-router';

import { GITHUB_URL } from '../../Constants';
import { DOCS, findDoc } from '../../docs';

export const Docs: FC = () => {
  const { slug } = useParams();
  const navigate = useNavigate();
  const doc = findDoc(slug);

  if (!doc) return <Navigate to='/docs' replace />;

  // The Markdown is rendered to plain anchors; keep links between pages inside the router.
  const onContentClick = (event: MouseEvent<HTMLDivElement>) => {
    const anchor = (event.target as HTMLElement).closest('a');
    const href = anchor?.getAttribute('href');
    if (
      !href?.startsWith('/') ||
      event.metaKey ||
      event.ctrlKey ||
      event.shiftKey
    )
      return;
    event.preventDefault();
    navigate(href);
  };

  return (
    <main className='app'>
      <section className='section docs'>
        <div className='inner'>
          <aside className='sidebar'>
            <nav aria-label='Docs'>
              <p className='label'>Docs</p>
              <ul>
                {DOCS.map((d) => (
                  <li key={d.slug}>
                    <Link
                      to={`/docs/${d.slug}`}
                      aria-current={d.slug === doc.slug ? 'page' : undefined}
                    >
                      {d.title}
                    </Link>
                    {d.slug === doc.slug && d.headings.length > 0 && (
                      <ul className='headings'>
                        {d.headings.map((h) => (
                          <li key={h.id}>
                            <Link to={`/docs/${d.slug}#${h.id}`}>{h.text}</Link>
                          </li>
                        ))}
                      </ul>
                    )}
                  </li>
                ))}
              </ul>
            </nav>
          </aside>

          <article className='doc'>
            <header>
              <p className='eyebrow'>Docs</p>
              <h1 className='title'>{doc.title}</h1>
              {doc.summary && <p className='subtitle'>{doc.summary}</p>}
            </header>
            <div
              className='prose'
              onClick={onContentClick}
              dangerouslySetInnerHTML={{ __html: doc.html }}
            />
            <p className='edit'>
              <a
                href={`${GITHUB_URL}/blob/master/docs/${doc.slug}.md`}
                target='_blank'
                rel='noreferrer'
              >
                View this page on GitHub
              </a>
            </p>
          </article>
        </div>
      </section>
    </main>
  );
};
