import type { FC } from 'react';
import { useEffect } from 'react';
import { Outlet, useLocation } from 'react-router';

import { SITE_NAME, SITE_URL } from '../../Constants';
import { docTitle } from '../../docs';
import { Footer } from '../Footer';
import { Nav } from '../Nav';

const titleOf = (pathname: string): string => {
  const doc = docTitle(pathname);
  return doc ? `${doc} - ${SITE_NAME}` : SITE_NAME;
};

export const Layout: FC = () => {
  const { pathname, hash } = useLocation();

  useEffect(() => {
    document.title = titleOf(pathname);

    const canonical = document.querySelector<HTMLLinkElement>(
      'link[rel="canonical"]',
    );
    if (canonical) {
      canonical.href = SITE_URL + (pathname === '/' ? '/' : pathname);
    }
  }, [pathname]);

  useEffect(() => {
    const target = hash ? document.getElementById(hash.slice(1)) : null;
    if (target) {
      target.scrollIntoView();
    } else {
      window.scrollTo(0, 0);
    }
  }, [pathname, hash]);

  return (
    <>
      <Nav />
      <Outlet />
      <Footer />
    </>
  );
};
