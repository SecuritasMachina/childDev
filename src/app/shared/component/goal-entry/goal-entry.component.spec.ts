import { async, ComponentFixture, TestBed } from '@angular/core/testing';

import { GoalEntryComponent } from './goal-entry.component';

describe('GoalEntryComponent', () => {
  let component: GoalEntryComponent;
  let fixture: ComponentFixture<GoalEntryComponent>;

  beforeEach(async(() => {
    TestBed.configureTestingModule({
      declarations: [ GoalEntryComponent ]
    })
    .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(GoalEntryComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
