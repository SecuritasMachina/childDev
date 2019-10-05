package com.ackdev.jdo.security;

import java.util.Date;

import com.googlecode.objectify.annotation.Cache;
import com.googlecode.objectify.annotation.Entity;
import com.googlecode.objectify.annotation.Id;
import com.googlecode.objectify.annotation.Index;
import com.googlecode.objectify.annotation.Unindex;

/**
 * Used for player history ex battles won, lost Game history/news
 */
@Entity
@Unindex
@Cache
public class History {
    private static final long serialVersionUID = 1L;
    @Id
    public Long id;
    @Index
    private String category;
    @Index
    private String deviceId;
    private String message;

    private double valueNbr;

    private Date valueDate;

    @Index
    private Date createdOn = new Date();

    private Date updatedOn;

    public Date expireDate = new org.joda.time.DateTime().plusYears(1).toDate();

    public History() {

    }

    public History(String pCategory, String pDeviceID, String pMessage) {
        this.deviceId = pDeviceID;
        this.message = pMessage;
        this.category = pCategory;
        new History(pCategory, pDeviceID, pMessage, 365);

    }

    public History(String pCategory, String pDeviceID, String pMessage, Integer expireDays) {
        this.deviceId = pDeviceID;
        this.message = pMessage;
        this.category = pCategory;
        if (expireDays != null)
            this.expireDate = new org.joda.time.DateTime().plusDays(expireDays.intValue()).toDate();

    }

    public String getCategory() {
        return this.category;
    }

    public Date getCreatedOn() {
        return this.createdOn;
    }

    public String getDeviceId() {
        return this.deviceId;
    }

    public String getMessage() {
        return this.message;
    }

    public Date getUpdatedOn() {
        return this.updatedOn;
    }

    public Date getValueDate() {
        return this.valueDate;
    }

    public double getValueNbr() {
        return this.valueNbr;
    }


    public void setCategory(final String category) {
        this.category = category;
    }

    public void setCreatedOn(final Date createdOn) {
        this.createdOn = createdOn;
    }

    public void setDeviceId(final String deviceId) {
        this.deviceId = deviceId;
    }

    public void setMessage(final String message) {
        this.message = message;
    }

    public void setUpdatedOn(final Date updatedOn) {
        this.updatedOn = updatedOn;
    }

    public void setValueDate(final Date valueDate) {
        this.valueDate = valueDate;
    }

    public void setValueNbr(final double valueNbr) {
        this.valueNbr = valueNbr;
    }

}
