import { useEffect, useRef, useState, type KeyboardEvent } from 'react';
import type { BusinessModel } from '@/api/types';
import { UiIcon } from '@/components/icons/UiIcon';
import { allFilters, filterId, filterLabel, type Filter } from './teamRows';

/**
 * The filter chips under the table's title ("role is Marketing Manager"), and the "+" that adds one. A chip is a button:
 * a tap takes the filter off. Filters of one kind are alternatives; the two kinds both have to hold.
 */
export function FilterBar({ model, filters, onChange }: { model: BusinessModel; filters: readonly Filter[]; onChange: (next: Filter[]) => void }) {
  const [open, setOpen] = useState(false);
  const [refocus, setRefocus] = useState(false);
  const addButton = useRef<HTMLButtonElement>(null);

  const available = allFilters(model).filter((candidate) => !filters.some((active) => filterId(active) === filterId(candidate)));

  // The control that had focus (a chip, or a menu item) is gone after the change, so focus goes to the "+".
  useEffect(() => {
    if (refocus) {
      addButton.current?.focus();
      setRefocus(false);
    }
  }, [refocus]);

  function add(filter: Filter) {
    onChange([...filters, filter]);
    setOpen(false);
    setRefocus(true);
  }

  function remove(filter: Filter) {
    onChange(filters.filter((active) => filterId(active) !== filterId(filter)));
    setRefocus(true);
  }

  function handleKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key === 'Escape' && open) {
      setOpen(false);
      setRefocus(true);
    }
  }

  return (
    <div className="filter-bar" role="group" aria-label="Filters" onKeyDown={handleKeyDown}>
      {filters.length > 0 ? (
        <ul className="filter-chips">
          {filters.map((filter) => (
            <li key={filterId(filter)}>
              <button className="filter-chip" type="button" aria-label={`Remove filter: ${filterLabel(filter)}`} onClick={() => remove(filter)}>
                {filterLabel(filter)}
                <UiIcon name="close" />
              </button>
            </li>
          ))}
        </ul>
      ) : null}

      {available.length > 0 ? (
        <div className="filter-add">
          <button
            className="filter-add-button"
            type="button"
            ref={addButton}
            aria-label="Add a filter"
            aria-expanded={open}
            aria-controls="filter-menu"
            onClick={() => setOpen((value) => !value)}
          >
            <UiIcon name="plus" />
          </button>
          {open ? (
            <ul className="filter-menu" id="filter-menu" aria-label="Add a filter">
              {available.map((filter) => (
                <li key={filterId(filter)}>
                  <button type="button" onClick={() => add(filter)}>
                    {filterLabel(filter)}
                  </button>
                </li>
              ))}
            </ul>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}
