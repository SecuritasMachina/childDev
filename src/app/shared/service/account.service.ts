import { Injectable, isDevMode } from '@angular/core';
import { Observable } from 'rxjs';
import { HttpClient } from '@angular/common/http';
import { CookieModule } from '../module/cookie/cookie.module';

export interface CommonStatusDTO {
    retSerObject: string;
    guid: string;
    jsonObj: string;
    status: string;
    message: string;
    nextPollSec: number; // When to poll again
}
export interface RegisterDTO {
    email: string;
    password: string;
    nickName: string;
    passwordHint: string;
    appTag: string;
}
export interface ResetPasswordDTO {
    oldPassword: string;
    newPassword: string;
}
export interface UserAccountDTO {
    guid: string;
    nickName: string;
    authHash: string;
    email: string;
    language: string;
    lastLoggedOn: number;
    lastLogonAttempt: number;
    timeZoneName: string;
    passwordHint: string;
    createdOn: number;
}
@Injectable( {
    providedIn: 'root'
} )

export class AccountService {
    baseURL = '';
    authToken = '';

    constructor( private http: HttpClient, private cookie: CookieModule) {
        this.authToken = cookie.getCookie( "token" )
        if ( isDevMode() ) {
            this.baseURL = '//localhost:8080/rest/account';
        } else {
            this.baseURL = 'https://child-dev.appspot.com/rest/account';
        }
    }

    register( pRegisterDTO: RegisterDTO ): Observable<UserAccountDTO> {
        return this.http.post<UserAccountDTO>( this.baseURL + '/register', pRegisterDTO );
    }

    get(): Observable<UserAccountDTO> {
        return this.http.get<UserAccountDTO>( this.baseURL + '/secure/getUser', { headers: { 'azAuthHeader': this.authToken } } );
    }
    checkAvailable(): Observable<CommonStatusDTO> {
        return this.http.get<CommonStatusDTO>( this.baseURL + '/secure/getUser', { headers: { 'azAuthHeader': this.authToken } } );
    }

    resetPW( pResetPasswordDTO: ResetPasswordDTO ) {
        return this.http.post( this.baseURL + '/resetPW', pResetPasswordDTO, { headers: { 'azAuthHeader': this.authToken } } );
    }

    token( nickname: string, password: string ): Observable<UserAccountDTO> {
        return this.http.get<UserAccountDTO>( this.baseURL + '/token/' + nickname + '/' + password );
    }

}
