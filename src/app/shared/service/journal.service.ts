import { Injectable, isDevMode } from '@angular/core';
import { Observable } from 'rxjs';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { CookieModule } from '../module/cookie/cookie.module';

export interface JournalItem {
    guid: string;
    accountFk: string;
    expirationDate: number;
    enteredDate: number;
    updatedOn: number;
    type: string;
    notes: string;
    locationText: string;
    activity: string;
    tags: string;
    mood: string;
}
@Injectable( {
    providedIn: 'root'
} )
export class JournalService {
    baseURL = '';
    token = '';
    //let headers = new HttpHeaders().set('azAuthHeader', hvalue1); // create header object

    constructor( private http: HttpClient, private cookie: CookieModule ) {
        this.token = cookie.getCookie( "token" )
        if ( isDevMode() ) {
            this.baseURL = '//localhost:8080/rest/secure/journal';
        } else {
            this.baseURL = 'https://child-dev.appspot.com/rest/secure/journal';
        }
    }

    update( pJournalItem: JournalItem ) {
        return this.http.post( this.baseURL + '/', pJournalItem, { headers: { 'azAuthHeader': this.token } } );
    }
    batchUpdate( pJournalItems: JournalItem[] ) {
        return this.http.post( this.baseURL + '/batch', pJournalItems, { headers: { 'azAuthHeader': this.token } } );
    }
    get( guid: string ): Observable<JournalItem> {
        return this.http.get<JournalItem>( this.baseURL + '/' + guid, { headers: { 'azAuthHeader': this.token } } );
    }
    getBatch( startTS: number, endTS: number ): Observable<JournalItem[]> {
        return this.http.get<JournalItem[]>( this.baseURL + '/range/' + startTS + "/" + endTS, { headers: { 'azAuthHeader': this.token } } );
    }
    delete( guid: string ) {
        return this.http.delete( this.baseURL + '/' + guid, { headers: { 'azAuthHeader': this.token } } );
    }
}
