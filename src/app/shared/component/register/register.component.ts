import { Component, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { AccountService, RegisterDTO, UserAccountDTO } from '../../service/account.service';
import { MatSnackBar } from '@angular/material';
import { OnDestroy } from "@angular/core";
import { FormControl, Validators } from '@angular/forms';
import { FormGroup } from "@angular/forms";
import { DataSource } from "@angular/cdk/collections";
import { Observable, BehaviorSubject } from "rxjs";
import { GlobalModule } from '../../module/global/global.module';
import { CookieModule } from '../../module/cookie/cookie.module';

@Component( {
    selector: 'app-register',
    templateUrl: './register.component.html',
    styleUrls: ['./register.component.css']
} )
export class RegisterComponent implements OnInit, OnDestroy {
    public ownerForm: FormGroup;
    public dataItem = {} as RegisterDTO;
    public showScanSpinner = false;
    public userAccountDTO = {} as UserAccountDTO;

    nickName = new FormControl();
    password = new FormControl();
    passwordHint = new FormControl();

    constructor( private rest: AccountService, private snackBar: MatSnackBar, public global: GlobalModule, public cookie: CookieModule ) { }

    ngOnInit() {
        this.ownerForm = new FormGroup( {
            nickName: new FormControl( '', [Validators.required] ),
            password: new FormControl( '', [Validators.required] ),
            passwordHint: new FormControl( '', [Validators.required] )
        } );
    }
    ngOnDestroy(): void {
        this.snackBar.dismiss();
    }
    load( id: string ) {
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
        this.dataItem.appTag = "childDev";
        this.dataItem.nickName = this.nickName.value;
        this.dataItem.password = this.password.value;
        this.dataItem.passwordHint = this.passwordHint.value;

        return this.rest.register( this.dataItem ).toPromise().then( res2 => {
            console.log( res2 );
            this.cookie.setCookie( "token", res2.authHash );
            this.cookie.setCookie( "accountFk", res2.guid );
            
            this.showScanSpinner = false;
            this.snackBar.open( 'Updated' );
        } );
    }

    hide() {
        //  this.showPage = false;
        // this.JournalListComponent.hide();
    }

}
