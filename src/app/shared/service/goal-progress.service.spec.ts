import { TestBed } from '@angular/core/testing';

import { GoalProgressService } from './goal-progress.service';

describe('GoalProgressService', () => {
  beforeEach(() => TestBed.configureTestingModule({}));

  it('should be created', () => {
    const service: GoalProgressService = TestBed.get(GoalProgressService);
    expect(service).toBeTruthy();
  });
});
