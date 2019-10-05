import { Injectable, isDevMode } from '@angular/core';
import { Observable } from 'rxjs';
import { HttpClient } from '@angular/common/http';
import { CookieModule } from '../module/cookie/cookie.module';
import { map } from 'rxjs/operators';

export interface GoalItem {
    guid: string;
    accountFk: string;
    goalText: string;
    nextMeetingDate: number;
    nextMeetingDateObj: Date;
    expirationDate: number;
    enteredDate: number;
    measureableOutcome: string;
    updatedOn: number;
    completionDate: number;
}
@Injectable( {
    providedIn: 'root'
} )
export class GoalService {
    token = '';
    baseURL = '';

    constructor( private http: HttpClient, private cookie: CookieModule ) {
        this.token = cookie.getCookie( "token" )
        console.log( this.token );
        if ( isDevMode() ) {
            this.baseURL = '//localhost:8080/rest/secure/goal';
        } else {
            this.baseURL = 'https://child-dev.appspot.com/rest/secure/goal';
        }
    }


    update( pGoalItem: GoalItem ) {
        return this.http.post( this.baseURL + '/', pGoalItem, { headers: { 'azAuthHeader': this.token } } );
    }
    batchUpdate( pGoalItems: GoalItem[] ) {
        return this.http.post( this.baseURL + '/batch', pGoalItems, { headers: { 'azAuthHeader': this.token } } );
    }

    get( guid: string ): Observable<GoalItem> {
        return this.http.get<GoalItem>( this.baseURL + '/' + guid, { headers: { 'azAuthHeader': this.token } } );
    }
    getBatch( startTS: number, endTS: number ): Observable<GoalItem[]> {
        return this.http.get<GoalItem[]>( this.baseURL + '/range/' + startTS + "/" + endTS, { headers: { 'azAuthHeader': this.token } } ).pipe(
            map(( data: GoalItem[] ) => data.map(( item: GoalItem ) => {
                const model = {} as GoalItem;
                Object.assign( model, item );
                
                if ( item.nextMeetingDate ) {
                    model.nextMeetingDateObj = new Date( item.nextMeetingDate );
                }
                if ( !item.goalText ) {
                    model.goalText = 'No text';
                } else if ( item.goalText['value'] ) {
                    model.goalText = item.goalText['value'];
                }

                return model;
            } ) )
        );
    }

    delete( guid: string ) {
        return this.http.delete( this.baseURL + '/' + guid, { headers: { 'azAuthHeader': this.token } } );
    }

}
