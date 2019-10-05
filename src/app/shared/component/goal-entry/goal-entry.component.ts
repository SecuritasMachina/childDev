import { Component, OnInit, ViewChild } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { GoalService, GoalItem } from '../../service/goal.service';
import { GoalProgressService, GoalItemProgress } from '../../service/goal-progress.service';
import { MatSnackBar } from '@angular/material';
import { OnDestroy } from "@angular/core";
import { FormControl, Validators } from '@angular/forms';
import { FormGroup } from "@angular/forms";
import { GlobalModule } from '../../module/global/global.module';
import { CookieModule } from '../../module/cookie/cookie.module';
//import 'date-fns';


@Component( {
    selector: 'app-goal-entry',
    templateUrl: './goal-entry.component.html',
    styleUrls: ['./goal-entry.component.css']
} )

export class GoalEntryComponent implements OnInit {

    constructor( private route: ActivatedRoute,
        private router: Router,
        private rest: GoalService,
        private restProgress: GoalProgressService,
        private snackBar: MatSnackBar,
        public global: GlobalModule,
        public cookie: CookieModule ) {
    }

    public ownerForm: FormGroup;
    public id: any;
    public dataItem = {} as GoalItem;
    public dataProgressItem = {} as GoalItemProgress;
    public showScanSpinner = false;
    goalText = new FormControl();
    nextStepItems = new FormControl();
    nextMeetingDate = new FormControl();

    ngOnInit() {
        this.goalText.setValue( '' );
        this.nextMeetingDate.setValue( new Date() );
        this.dataItem.enteredDate = new Date().getTime();
        this.id = this.route.snapshot.paramMap.get( 'id' );

        if ( this.id ) {
            this.load( this.id );
        }
        this.ownerForm = new FormGroup( {
            notes: new FormControl(),
            nextStepItems: new FormControl(),
            nextMeetingDate: new FormControl()
        } );
    }


    load( id: string ) {
        this.goalText.setValue( '' );
        this.rest.get( id )
            .subscribe( dataItem => {
                this.dataItem = dataItem;
                if ( dataItem.goalText )
                    this.goalText.setValue( dataItem.goalText );
                //if ( dataItem.nextSteps )
                  //  this.nextSteps.setValue( dataItem.nextSteps );
            } );
    }

    undo() {
    }

    save() {
        this.showScanSpinner = true;
        if ( !this.dataItem.guid ) {
            this.dataItem.guid = this.global.uuidv4();
        }
        this.dataItem.accountFk = this.cookie.getCookie( 'accountFk' );
        this.dataItem.goalText = this.goalText.value;
        this.dataItem.nextMeetingDate = this.global.date2MS(this.nextMeetingDate.value);
        console.log(this.dataItem);
        return this.rest.update( this.dataItem ).toPromise().then( res2 => {
            if ( !this.dataProgressItem.guid ) {
                this.dataProgressItem.guid = this.global.uuidv4();
            }
            this.dataProgressItem.goalFk = this.dataItem.guid;
            this.dataProgressItem.accountFk = this.dataItem.accountFk;
            this.dataProgressItem.nextStepItems = this.nextStepItems.value;
            
            this.dataProgressItem.nextMeetingDate = this.global.date2MS(this.nextMeetingDate.value);
            this.restProgress.update( this.dataProgressItem ).toPromise().then( res3 => {
                this.showScanSpinner = false;
                this.snackBar.open( 'Updated' );
            } );
        } );
    }
}
