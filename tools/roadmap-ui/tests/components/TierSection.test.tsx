import { render } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import { TierSection } from '../../src/components/TierSection';

describe('TierSection', () => {
  const mockTasks = [
    {
      id: "TEST-01",
      title: "Test Task",
      domain: "Syntax",
      category: "Expression",
      status: "incomplete" as const,
      metrics: { blocker: 3, difficulty: 2, generality: 4, score: 24 },
    }
  ];

  it('matches snapshot with tasks', () => {
    const { container } = render(
      <TierSection
        tier={1}
        title="Tier 1"
        tasks={mockTasks}
        cartIds={[]}
        selectedTaskId={null}
        filterKey="all"
        onCartClick={vi.fn()}
        onStatusChange={vi.fn()}
        toggleTierCart={vi.fn()}
      />
    );
    expect(container.innerHTML).toMatchSnapshot();
  });

  it('matches snapshot with empty tasks', () => {
    const { container } = render(
      <TierSection
        tier={2}
        title="Tier 2"
        tasks={[]}
        cartIds={[]}
        selectedTaskId={null}
        filterKey="all"
        onCartClick={vi.fn()}
        onStatusChange={vi.fn()}
        toggleTierCart={vi.fn()}
      />
    );
    expect(container.innerHTML).toMatchSnapshot();
  });
});
