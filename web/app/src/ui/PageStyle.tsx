import { useLayoutEffect } from 'react';

/**
 * CSS конкретной страницы дизайна. В дизайне у каждой HTML-страницы свой набор стилей, и они пересекаются
 * (одинаковые классы, правила для html/body), поэтому подключаем стиль страницы, только пока она открыта:
 *   import css from '../styles/pages/students.css?inline';
 *   <PageStyle css={css} />
 */
export function PageStyle({ css }: { css: string | string[] }) {
  const text = Array.isArray(css) ? css.join('\n') : css;
  useLayoutEffect(() => {
    const el = document.createElement('style');
    el.dataset.pageStyle = '';
    el.textContent = text;
    document.head.appendChild(el);
    return () => el.remove();
  }, [text]);
  return null;
}
