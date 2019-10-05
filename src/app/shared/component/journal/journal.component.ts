import { Component, OnInit, ViewChild } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { JournalService, JournalItem } from '../../service/journal.service';
import { JournalListComponent } from '../journal-list/journal-list.component';
import { MatSnackBar } from '@angular/material';
import { OnDestroy } from "@angular/core";
import { FormControl, Validators } from '@angular/forms';
import { FormGroup } from "@angular/forms";

@Component( {
    selector: 'app-journal',
    templateUrl: './journal.component.html',
    styleUrls: ['./journal.component.css']
} )

export class JournalComponent implements OnInit, OnDestroy {
    constructor( private route: ActivatedRoute,
        private router: Router,
        private rest: JournalService, private snackBar: MatSnackBar ) {
    }
    
    ngOnInit() {
        this.dataItem.notes = "Loading";
        this.id = this.route.snapshot.paramMap.get( 'id' );
        if ( this.id ) {
            this.load( this.id );
        }
        this.ownerForm = new FormGroup( {
            notes: new FormControl()
        } );
    }
    ngOnDestroy(): void {
        this.snackBar.dismiss();
    }

    //@ViewChild( JournalListComponent )
    private JournalListComponent: JournalListComponent;
    public ownerForm: FormGroup;
    public id: any;
    public dataItem = {} as JournalItem;
    public showPage = false;
    public showScanSpinner = false;
    notes = new FormControl();
    tags = new FormControl();
    
    onEditParentDS( p: JournalItem ) {
    }


    load( id: string ) {
        this.showPage = true;
        /*this.rest.getTable( id )
        .subscribe( dataItem => {
            this.dataItem = dataItem;
            this.notes.setValue(dataItem.notes);
            this.tags.setValue(dataItem.tags);
            this.JournalListComponent.load( id );
        } );
        */
    }

    save() {
        this.showScanSpinner = true;
        this.dataItem.notes = this.notes.value;
        this.dataItem.tags = this.tags.value;

        return this.rest.update( this.dataItem ).toPromise().then( res2 => {
            this.showScanSpinner = false;
            this.snackBar.open( 'Updated' );
        } );
    }

    hide() {
        this.showPage = false;
        // this.JournalListComponent.hide();
    }
}
