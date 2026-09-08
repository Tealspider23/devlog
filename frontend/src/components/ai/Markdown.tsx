import ReactMarkdown from 'react-markdown'

/** Model output is the one string in this app devlog did not author — react-markdown renders to React elements only, no dangerouslySetInnerHTML, so it cannot inject HTML. */
export function Markdown({ source }: { source: string }) {
  return (
    <ReactMarkdown
      components={{
        h1: (p) => <h2 className="text-sm font-semibold text-ink" {...p} />,
        h2: (p) => <h2 className="text-sm font-semibold text-ink" {...p} />,
        h3: (p) => <h3 className="text-xs font-semibold text-muted" {...p} />,
        p: (p) => <p className="text-sm leading-relaxed text-muted" {...p} />,
        ul: (p) => <ul className="ml-4 flex list-disc flex-col gap-1 text-sm text-muted" {...p} />,
        ol: (p) => <ol className="ml-4 flex list-decimal flex-col gap-1 text-sm text-muted" {...p} />,
        li: (p) => <li {...p} />,
        strong: (p) => <strong className="font-semibold text-ink" {...p} />,
        code: (p) => <code className="rounded bg-raised px-1 py-0.5 text-[11px] text-ink" {...p} />,
        table: (p) => <table className="border-collapse border border-line text-xs" {...p} />,
        th: (p) => <th className="border border-line px-2 py-1 text-left text-faint" {...p} />,
        td: (p) => <td className="border border-line px-2 py-1 text-muted" {...p} />,
      }}
    >
      {source}
    </ReactMarkdown>
  )
}
