interface PaginationProps {
  page: number
  totalPages: number
  /** While the next page is on its way, so a second click cannot overtake the first. */
  busy: boolean
  /** Names the navigation landmark, so two lists on one page can be told apart. */
  label: string
  onPageChange: (page: number) => void
}

/** Previous and next under a list, or nothing at all when the list fits on one page. */
export function Pagination({ page, totalPages, busy, label, onPageChange }: PaginationProps) {
  if (totalPages <= 1) return null

  return (
    <nav className="pagination" aria-label={label}>
      <button
        type="button"
        className="pagination-button"
        onClick={() => onPageChange(page - 1)}
        disabled={page <= 1 || busy}
      >
        ‹ Previous
      </button>
      <span className="pagination-status">
        Page {page} of {totalPages}
      </span>
      <button
        type="button"
        className="pagination-button"
        onClick={() => onPageChange(page + 1)}
        disabled={page >= totalPages || busy}
      >
        Next ›
      </button>
    </nav>
  )
}
