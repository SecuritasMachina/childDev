import { BrowserModule } from '@angular/platform-browser';
import { NgModule } from '@angular/core';
import { FormsModule } from "@angular/forms";

import { ReactiveFormsModule } from '@angular/forms'; // <-- NgModel lives here
import { AppRoutingModule } from './app-routing.module';
import { AppComponent } from './app.component';
import { BrowserAnimationsModule } from '@angular/platform-browser/animations';
import { HttpClientModule } from '@angular/common/http';
import { TreeviewModule } from 'ngx-treeview';
import { SQLite } from '@ionic-native/sqlite/ngx';
import { Toast } from '@ionic-native/toast/ngx';
import { IonicModule, IonicRouteStrategy } from '@ionic/angular';
import { SplashScreen } from '@ionic-native/splash-screen/ngx';
import { StatusBar } from '@ionic-native/status-bar/ngx';



import {
    MAT_DIALOG_DEFAULT_OPTIONS,
    MAT_SNACK_BAR_DEFAULT_OPTIONS
} from '@angular/material';

import { ScrollDispatchModule } from "@angular/cdk/scrolling";
import { AppMaterialModules } from './material.module';
import { JournalComponent } from './shared/component/journal/journal.component';
import { GoalComponent } from './shared/component/goal/goal.component';
import { JournalEntryComponent } from './shared/component/journal-entry/journal-entry.component';
import { JournalListComponent } from './shared/component/journal-list/journal-list.component';
import { GoalListComponent } from './shared/component/goal-list/goal-list.component';
import { GoalEntryComponent } from './shared/component/goal-entry/goal-entry.component';
import { TodoListComponent } from './shared/component/todo-list/todo-list.component';
import { TodoComponent } from './shared/component/todo/todo.component';
import { AboutComponent } from './shared/component/about/about.component';
import { SettingsComponent } from './shared/component/settings/settings.component';
import { DefaultComponent } from './shared/component/default/default.component';
import { RegisterComponent } from './shared/component/register/register.component';
import { GlobalModule } from './shared/module/global/global.module';
import { SignonComponent } from './shared/component/signon/signon.component';
import { CookieModule } from './shared/module/cookie/cookie.module';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { EncrDecrService } from './shared/util/encr-decr.service';


@NgModule( {
    declarations: [
        AppComponent,
        JournalComponent,
        GoalComponent,
        JournalEntryComponent,
        JournalListComponent,
        GoalListComponent,
        GoalEntryComponent,
        TodoListComponent,
        TodoComponent,
        AboutComponent,
        SettingsComponent,
        DefaultComponent,
        RegisterComponent,
        SignonComponent
    ],
    imports: [
        BrowserModule,
        AppRoutingModule,
        BrowserAnimationsModule,
        HttpClientModule,
        TreeviewModule.forRoot(),
        FormsModule,
        ReactiveFormsModule,
        ScrollDispatchModule,
        AppMaterialModules,
        GlobalModule,
        CookieModule,
        MatDatepickerModule
    ],
    providers: [EncrDecrService,
        StatusBar,
        SplashScreen,
        SQLite,
        Toast,
        { provide: MAT_SNACK_BAR_DEFAULT_OPTIONS, useValue: { duration: 2500 } },
        { provide: MAT_DIALOG_DEFAULT_OPTIONS, useValue: { hasBackdrop: false } }],

    bootstrap: [AppComponent]
} )
export class AppModule { }
