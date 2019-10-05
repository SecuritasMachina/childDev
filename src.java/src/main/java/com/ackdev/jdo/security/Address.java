package com.ackdev.jdo.security;

import java.io.Serializable;
import java.util.Date;

import com.googlecode.objectify.annotation.Cache;
import com.googlecode.objectify.annotation.Entity;
import com.googlecode.objectify.annotation.Id;
import com.googlecode.objectify.annotation.Index;
import com.googlecode.objectify.annotation.OnLoad;
import com.googlecode.objectify.annotation.OnSave;

@Entity
@Index
@Cache
public class Address implements Serializable {
    private static final long serialVersionUID = 1L;


    /**
     * Old fields below
     */
    @Id
    public Long id;

    public String city;

    public String state;
    public String postalCode;
    public String line1;
    public String line2;
    public double createdLat;
    public double createdLong;
    public Date createdOn = new Date();
    public Date updatedOn;

    @OnLoad
    void onLoad() { /* do something after load */
    }

    @OnSave
    void onPersist() { /* do something before persisting */

    }

}
