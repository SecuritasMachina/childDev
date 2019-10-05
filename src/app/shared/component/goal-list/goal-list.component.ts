import { Component, OnInit, ViewChild } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { GoalService, GoalItem } from '../../service/goal.service';
import { MatSnackBar } from '@angular/material';
import { OnDestroy } from "@angular/core";
import { FormControl, Validators } from '@angular/forms';
import { FormGroup } from "@angular/forms";
import { DataSource } from "@angular/cdk/collections";
import { Observable, BehaviorSubject } from "rxjs";
import { map } from 'rxjs/operators';

@Component( {
    selector: 'app-goal-list',
    templateUrl: './goal-list.component.html',
    styleUrls: ['./goal-list.component.css']
} )

export class GoalListComponent implements OnInit {

    constructor( public rest: GoalService ) { }
    dataSource: GoalItem[];
    
    ngOnInit() {
        this.load();
    }
    load() {
        this.rest.getBatch( 0, new Date().getTime() ).subscribe( items => {
            this.dataSource = items;
        } );
    }
}

export class ComponentDataSource extends DataSource<GoalItem> {
    displayedColumns = ['guid', 'notes'];
    private dataSubject = new BehaviorSubject<GoalItem[]>( [] );

    data() {
        return this.dataSubject.value;
    }

    update( data ) {
        this.dataSubject.next( data );
    }

    constructor( data: GoalItem[] ) {
        super();
        this.dataSubject.next( data );
    }

    /** Connect function called by the table to retrieve one stream containing the data to render. */
    connect(): Observable<GoalItem[]> {
        return this.dataSubject;
    }

    disconnect() { }
}
