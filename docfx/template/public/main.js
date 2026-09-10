/*
 * DotnetTokenKiller documentation theme behaviour.
 *
 * Uses only docfx's documented `main.js` extension points (`defaultTheme`,
 * `iconLinks`, `start`) so a docfx upgrade does not break it.
 */

/** Copies the install command, then confirms it on the button itself. */
function wireCopyButtons() {
  for (const button of document.querySelectorAll('.dtk-copy')) {
    button.addEventListener('click', async () => {
      const text = button.parentElement?.querySelector('code')?.textContent
      if (!text) {
        return
      }

      try {
        await navigator.clipboard.writeText(text.trim())
      } catch {
        button.textContent = 'Press Ctrl+C'
        return
      }

      const label = button.dataset.label ?? button.textContent
      button.dataset.label = label
      button.dataset.copied = 'true'
      button.textContent = 'Copied'
      setTimeout(() => {
        button.dataset.copied = 'false'
        button.textContent = label
      }, 1600)
    })
  }
}

/*
 * The landing page's single piece of non-user-triggered motion: the raw pane's
 * noise fades back and the filtered pane arrives, once, on load. The markup
 * ships in its final state, so this is inert when the animation cannot run.
 */
function playFilterSequence() {
  const diff = document.querySelector('.dtk-diff')
  if (!diff || window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
    return
  }

  diff.dataset.animate = 'pending'
  requestAnimationFrame(() => {
    requestAnimationFrame(() => {
      diff.dataset.animate = 'running'
    })
  })
}

function enhance() {
  wireCopyButtons()
  playFilterSequence()
}

export default {
  defaultTheme: 'dark',

  iconLinks: [
    {
      icon: 'github',
      href: 'https://github.com/HandyS11/DotnetTokenKiller',
      title: 'Source on GitHub',
    },
    {
      icon: 'box-seam',
      href: 'https://www.nuget.org/packages/DotnetTokenKiller',
      title: 'Package on NuGet',
    },
  ],

  start: () => {
    if (document.readyState === 'loading') {
      document.addEventListener('DOMContentLoaded', enhance, { once: true })
    } else {
      enhance()
    }
  },
}
