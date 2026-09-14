import { render } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import { ControlsBar } from '../../src/components/ControlsBar';

describe('ControlsBar', () => {
  it('matches snapshot', () => {
    const { container } = render(
      <ControlsBar
        searchQuery=""
        setSearchQuery={vi.fn()}
        progressFilter="all"
        setProgressFilter={vi.fn()}
        showAdvancedFilters={false}
        setShowAdvancedFilters={vi.fn()}
        historyLengthPast={0}
        historyLengthFuture={0}
        handleUndo={vi.fn()}
        handleRedo={vi.fn()}
        saveStatus="saved"
        cartIdsCount={0}
        copyJulesPrompt={vi.fn()}
        copyCartIdsOnly={vi.fn()}
        setCartIds={vi.fn()}
        setToast={vi.fn()}
        minBlocker={0}
        setMinBlocker={vi.fn()}
        minDifficulty={0}
        setMinDifficulty={vi.fn()}
        minGenerality={0}
        setMinGenerality={vi.fn()}
      />
    );
    expect(container.innerHTML).toMatchSnapshot();
  });
});
