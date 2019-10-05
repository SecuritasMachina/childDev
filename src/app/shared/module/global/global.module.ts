import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';



@NgModule( {
    declarations: [],
    imports: [
        CommonModule
    ]
} )
export class GlobalModule {

    uuidv4(): string {
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace( /[xy]/g, function( c ) {
            var r = Math.random() * 16 | 0, v = c == 'x' ? r : ( r & 0x3 | 0x8 );
            return v.toString( 16 );
        } );
    }
    date2MS(pDate:string): number {
        var dt:Date=new Date(pDate);
        return dt.getTime();
    }

}
