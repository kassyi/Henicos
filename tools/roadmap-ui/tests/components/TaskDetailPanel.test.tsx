import { render } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { TaskDetailPanel } from '../../src/components/TaskDetailPanel';

describe('TaskDetailPanel', () => {
  beforeEach(() => {
    global.ResizeObserver = class ResizeObserver {
      observe() {}
      unobserve() {}
      disconnect() {}
    };
  });

  const mockTask = {
    id: "TEST-01",
    title: "Test Task",
    domain: "Syntax",
    category: "Expression",
    status: "incomplete" as const,
    metrics: { blocker: 3, difficulty: 2, generality: 4, score: 24 },
    details: {
      csharp_example: "var x = 1;",
      current_behavior: "Nothing",
      target_cuda: "int x = 1;",
      notes: "Test notes"
    },
    pr_url: "https://github.com/foo/bar/pull/1",
    pr_number: 1
  };

  it('matches snapshot in bottom dock (horizontal)', () => {
    const { container } = render(
      <TaskDetailPanel
        task={mockTask}
        dockPosition="bottom"
        onDockPositionChange={vi.fn()}
        onStatusChange={vi.fn()}
        onNotesChange={vi.fn()}
      />
    );
    expect(container.innerHTML).toMatchSnapshot();
  });

  it('matches snapshot in left dock (vertical)', () => {
    const { container } = render(
      <TaskDetailPanel
        task={mockTask}
        dockPosition="left"
        onDockPositionChange={vi.fn()}
        onStatusChange={vi.fn()}
        onNotesChange={vi.fn()}
      />
    );
    expect(container.innerHTML).toMatchSnapshot();
  });

  it('matches snapshot when task is null', () => {
    const { container } = render(
      <TaskDetailPanel
        task={null}
        dockPosition="bottom"
        onDockPositionChange={vi.fn()}
        onStatusChange={vi.fn()}
        onNotesChange={vi.fn()}
      />
    );
    expect(container.innerHTML).toMatchSnapshot();
  });
});
