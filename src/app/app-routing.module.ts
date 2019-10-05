import { NgModule } from '@angular/core';
import { Routes, RouterModule } from '@angular/router';
import { GoalListComponent } from './shared/component/goal-list/goal-list.component';
import { GoalEntryComponent } from './shared/component/goal-entry/goal-entry.component';
import { SettingsComponent } from './shared/component/settings/settings.component';
import { JournalComponent } from './shared/component/journal/journal.component';
import { TodoListComponent } from './shared/component/todo-list/todo-list.component';
import { TodoComponent } from './shared/component/todo/todo.component';
import { AboutComponent } from './shared/component/about/about.component';
import { DefaultComponent } from './shared/component/default/default.component';
import { SignonComponent } from './shared/component/signon/signon.component';
import { RegisterComponent } from './shared/component/register/register.component';
import { GoalComponent } from './shared/component/goal/goal.component';
const routes: Routes = [
    { path: 'signon', component: SignonComponent },
    { path: 'register', component: RegisterComponent },
    { path: 'goal-list/goal-entry', component: GoalEntryComponent },
    { path: 'goal-list/goal-entry/:id', component: GoalEntryComponent },
    { path: 'goal-list/goal/:id', component: GoalComponent },
    { path: 'goal-list', component: GoalListComponent },
    { path: 'journal', component: JournalComponent },
    { path: 'todo-list', component: TodoListComponent },
    { path: 'todo-list/todo', component: TodoComponent },
    { path: 'settings', component: SettingsComponent },
    { path: 'about', component: AboutComponent },
    { path: '', component: DefaultComponent }
];

@NgModule( {
    imports: [RouterModule.forRoot( routes, { enableTracing: false } )],
    exports: [RouterModule]
} )
export class AppRoutingModule { }
