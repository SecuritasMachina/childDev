import { Injectable, isDevMode } from '@angular/core';
import { Observable } from 'rxjs';
import { HttpClient } from '@angular/common/http';
import { CookieModule } from '../module/cookie/cookie.module';


export interface GoalItemProgress {
    guid: string;
    accountFk: string;
    goalFk: string;
    nextMeetingDate:number;
    expirationDate: number;
    completedItems: string;
    location: string;
    nextStepItems: string;
    nextLocation: string;
    attendees: string;
    notes: string;
    updatedOn: number;
    tags: string;
}
@Injectable( {
    providedIn: 'root'
} )
export class GoalProgressService {
    token = '';
    baseURL = '';

    constructor( private http: HttpClient, private cookie: CookieModule ) {
        this.token = cookie.getCookie( "token" )
        if ( isDevMode() ) {
            this.baseURL = '//localhost:8080/rest/secure/goalProgress';
        } else {
            this.baseURL = 'https://child-dev.appspot.com/secure/goalProgress';
        }
    }


    update( pGoalItemProgress: GoalItemProgress ) {
        return this.http.post( this.baseURL + '/', pGoalItemProgress, { headers: { 'azAuthHeader': this.token } } );
    }
    batchUpdate( pGoalItemProgresss: GoalItemProgress[] ) {
        return this.http.post( this.baseURL + '/batch', pGoalItemProgresss, { headers: { 'azAuthHeader': this.token } } );
    }

    get( guid: string ): Observable<GoalItemProgress> {
        return this.http.get<GoalItemProgress>( this.baseURL + '/' + guid, { headers: { 'azAuthHeader': this.token } } );
    }
    getBatch( startTS: number, endTS: number ): Observable<GoalItemProgress[]> {
        return this.http.get<GoalItemProgress[]>( this.baseURL + '/range/' + startTS + "/" + endTS, { headers: { 'azAuthHeader': this.token } } );
    }

    delete( guid: string ) {
        return this.http.delete( this.baseURL + '/' + guid, { headers: { 'azAuthHeader': this.token } } );
    }
}
