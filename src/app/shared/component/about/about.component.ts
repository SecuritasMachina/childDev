import { Component, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { AccountService, RegisterDTO, UserAccountDTO } from '../../service/account.service';
import { MatSnackBar } from '@angular/material';
import { OnDestroy } from "@angular/core";
import { FormControl, Validators } from '@angular/forms';
import { FormGroup } from "@angular/forms";
import { DataSource } from "@angular/cdk/collections";
import { Observable, BehaviorSubject } from "rxjs";
import { GlobalModule } from '../../module/global/global.module';

@Component({
  selector: 'app-about',
  templateUrl: './about.component.html',
  styleUrls: ['./about.component.css']
})
export class AboutComponent implements OnInit {

  constructor() { }

  ngOnInit() {
  }

}
