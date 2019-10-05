import { Component, OnInit } from '@angular/core';
import { AccountService, RegisterDTO, UserAccountDTO } from '../../service/account.service';
import { CookieModule } from '../../module/cookie/cookie.module';
import { GlobalModule } from '../../module/global/global.module';

@Component( {
    selector: 'app-default',
    templateUrl: './default.component.html',
    styleUrls: ['./default.component.css']
} )
export class DefaultComponent implements OnInit {

    constructor( private rest: AccountService, public cookie: CookieModule, public global: GlobalModule ) { }

    ngOnInit() {
        this.load();
    }
    load() {

        this.rest.get()
            .subscribe( dataItem => {
                this.cookie.setCookie( 'accountFk', dataItem.guid );
            } );

    }
}
