import { Component, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { AccountService, RegisterDTO, UserAccountDTO } from '../../service/account.service';
import { MatSnackBar } from '@angular/material';
import { OnDestroy } from "@angular/core";
import { FormControl, Validators } from '@angular/forms';
import { FormGroup } from "@angular/forms";
import { DataSource } from "@angular/cdk/collections";
import { Observable, BehaviorSubject } from "rxjs";


@Component( {
    selector: 'app-settings',
    templateUrl: './settings.component.html',
    styleUrls: ['./settings.component.css']
} )
export class SettingsComponent implements OnInit, OnDestroy {
    public ownerForm: FormGroup;
    public dataItem = {} as RegisterDTO;
    public showScanSpinner = false;
    public userAccountDTO = {} as UserAccountDTO;
    nickName = new FormControl();
    password = new FormControl();
    passwordHint = new FormControl();

    constructor( private rest: AccountService, private snackBar: MatSnackBar ) { }

    ngOnInit() {
        this.ownerForm = new FormGroup( {
            nickName: new FormControl( '', [Validators.required] ),
            password: new FormControl( '', [Validators.required] )
        } );
        this.load();
    }
    ngOnDestroy(): void {
        this.snackBar.dismiss();
    }
    load() {
        this.rest.get()
            .subscribe( dataItem => {
                this.userAccountDTO = dataItem;
                this.nickName.setValue( dataItem.nickName );
                // this.password.setValue( dataItem.password );
                this.passwordHint.setValue( dataItem.passwordHint );
                //this.JournalListComponent.load( id );
            } );

    }
    undo() {

    }
    save() {
        this.showScanSpinner = true;
        this.dataItem.nickName = this.nickName.value;
        this.dataItem.password = this.password.value;
        this.dataItem.passwordHint = this.passwordHint.value;

        return this.rest.register( this.dataItem ).toPromise().then( res2 => {
            this.showScanSpinner = false;
            this.snackBar.open( 'Updated' );
        } );
    }

    hide() {
        //  this.showPage = false;
        // this.JournalListComponent.hide();
    }

}
