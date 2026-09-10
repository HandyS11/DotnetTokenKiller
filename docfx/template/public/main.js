/*
 * DotnetTokenKiller documentation theme behaviour.
 *
 * Uses only docfx's documented `main.js` extension points (`defaultTheme`,
 * `iconLinks`, `start`) so a docfx upgrade does not break it.
 */

const COPY_SHORTCUT = /Mac|iPhone|iPad/.test(navigator.userAgent)
  ? '⌘C'
  : 'Ctrl+C'

/** Puts a node's text under the user's selection, ready for a manual copy. */
function selectContents(node) {
  const range = document.createRange()
  range.selectNodeContents(node)
  const selection = window.getSelection()
  selection.removeAllRanges()
  selection.addRange(range)
}

/** Copies the adjacent command, then reports the result on the button itself. */
function wireCopyButtons() {
  for (const button of document.querySelectorAll('.dtk-copy')) {
    const code = button.parentElement?.querySelector('code')
    if (!code) {
      continue
    }

    const label = button.textContent
    let revertTimer

    /* Every outcome reverts to the original label, so the button can never be
       left showing a stale message. */
    const report = (message, copied, revertAfterMs) => {
      clearTimeout(revertTimer)
      button.textContent = message
      button.dataset.copied = copied
      revertTimer = setTimeout(() => {
        button.textContent = label
        button.dataset.copied = 'false'
      }, revertAfterMs)
    }

    button.addEventListener('click', async () => {
      try {
        await navigator.clipboard.writeText(code.textContent.trim())
        report('Copied', 'true', 1600)
      } catch {
        /* Clipboard access can be refused - over plain HTTP, or by permission.
           Select the command first so the shortcut has something to act on. */
        selectContents(code)
        report(`Press ${COPY_SHORTCUT}`, 'false', 4000)
      }
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
