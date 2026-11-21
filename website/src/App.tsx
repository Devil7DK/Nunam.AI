import './App.css';

import type { FC } from 'react';
import { BrowserRouter, Route, Routes } from 'react-router';

import { Background, Layout, ThemeProvider } from './components';
import { Docs, Home } from './pages';

export const App: FC = () => (
  <ThemeProvider>
    <Background />
    <BrowserRouter>
      <Routes>
        <Route element={<Layout />}>
          <Route index element={<Home />} />
          <Route path='docs/:slug?' element={<Docs />} />
          <Route path='*' element={<Home />} />
        </Route>
      </Routes>
    </BrowserRouter>
  </ThemeProvider>
);
