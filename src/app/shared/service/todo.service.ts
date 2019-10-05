import { Injectable, isDevMode } from '@angular/core';
import { Observable } from 'rxjs';
import { HttpClient } from '@angular/common/http';
import { CookieModule } from '../module/cookie/cookie.module';


@Injectable( {
    providedIn: 'root'
} )
export class TodoService {
    token = '';
    baseURL = '';

    constructor( private http: HttpClient, private cookie: CookieModule ) {
        this.token = cookie.getCookie( "token" )
        if ( isDevMode() ) {
            this.baseURL = '//localhost:8080/rest/secure/journal';
        } else {
            this.baseURL = 'https://child-dev.appspot.com/rest/secure/journal';
        }
    }
}
