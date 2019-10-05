import { Component, OnInit, ViewChild } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { GoalService, GoalItem } from '../../service/goal.service';
import { MatSnackBar } from '@angular/material';
import { OnDestroy } from "@angular/core";
import { FormControl, Validators } from '@angular/forms';
import { FormGroup } from "@angular/forms";
import { GlobalModule } from '../../module/global/global.module';
import { CookieModule } from '../../module/cookie/cookie.module';

@Component( {
    selector: 'app-goal',
    templateUrl: './goal.component.html',
    styleUrls: ['./goal.component.css']
} )
export class GoalComponent implements OnInit {

    constructor( private route: ActivatedRoute,
        private router: Router,
        private rest: GoalService,
        private snackBar: MatSnackBar,
        public global: GlobalModule,
        public cookie: CookieModule ) {
    }

    ngOnInit() {
        this.id = this.route.snapshot.paramMap.get( 'id' );
        if ( this.id ) {
            this.load( this.id );
        }
        this.ownerForm = new FormGroup( {
            goalText: new FormControl(),
            nextSteps: new FormControl()
        } );
    }
    public ownerForm: FormGroup;
    public id: any;
    public dataItem = {} as GoalItem;
    public showPage = false;
    public showScanSpinner = false;
    goalText = new FormControl();
    nextSteps = new FormControl();
    tags = new FormControl();

    load( id: string ) {
        this.showPage = true;
        this.goalText.setValue( '' );
        this.rest.get( id )
            .subscribe( dataItem => {
                this.dataItem = dataItem;
                if ( dataItem.goalText )
                    this.goalText.setValue( dataItem.goalText );
//                if ( dataItem.nextSteps )
//                    this.nextSteps.setValue( dataItem.nextSteps );
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
//        this.dataItem.nextSteps = this.nextSteps.value;

        //        guid: string;
        //        accountFk: string;
        //        expirationDate: number;
        //        enteredDate: Date;
        //        measureableOutcome: string;
        //        nextSteps: string;
        //        goalText: string;
        //        updatedOn: number;
        //        completionDate: number;
        //        tags: string;

        return this.rest.update( this.dataItem ).toPromise().then( res2 => {
            this.showScanSpinner = false;
            this.snackBar.open( 'Updated' );
        } );
    }

}
