import { Component, OnInit, ViewChild } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { JournalService, JournalItem } from '../../service/journal.service';
import { MatSnackBar } from '@angular/material';
import { OnDestroy } from "@angular/core";
import { FormControl, Validators } from '@angular/forms';
import { FormGroup } from "@angular/forms";
import { DataSource } from "@angular/cdk/collections";
import { Observable, BehaviorSubject } from "rxjs";

@Component( {
    selector: 'app-journal-list',
    templateUrl: './journal-list.component.html',
    styleUrls: ['./journal-list.component.css']
} )
export class JournalListComponent implements OnInit {

  constructor() { }

  ngOnInit() {
  }

}
export class ComponentDataSource extends DataSource<JournalItem> {

    private dataSubject = new BehaviorSubject<JournalItem[]>( [] );

    data() {
        return this.dataSubject.value;
    }

    update( data ) {
        this.dataSubject.next( data );
    }

    constructor( data: JournalItem[] ) {
        super();
        this.dataSubject.next( data );
    }

    /** Connect function called by the table to retrieve one stream containing the data to render. */
    connect(): Observable<JournalItem[]> {
        return this.dataSubject;
    }

    disconnect() { }
}