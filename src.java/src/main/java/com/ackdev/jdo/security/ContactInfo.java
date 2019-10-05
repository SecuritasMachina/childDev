package com.ackdev.jdo.security;

import java.util.Date;

import com.ackdev.common.statics.EnumContactType;
import com.googlecode.objectify.annotation.Cache;
import com.googlecode.objectify.annotation.Entity;
import com.googlecode.objectify.annotation.Id;
import com.googlecode.objectify.annotation.Index;
import com.googlecode.objectify.annotation.Unindex;

/* Billing info */
@Entity
@Unindex
@Cache
public class ContactInfo {
    private static final long serialVersionUID = 1L;


    @Id
    public Long id;

    @Index
    public Long accountIdFk;

    private Long parentKey;
    @Index
    private String deviceID;
    @Index
    public Long teleID;
    @Index
    private String name;
    @Index
    private String addressLine1;
    private String addressLine2;
    @Index
    private String city;
    @Index
    private String state;
    @Index
    private String postalCode;
    @Index
    private String country;
    @Index
    private String password;
    @Index
    private String email;
    private String phoneNumber;
    // Payment details
    private String paymentType;
    private String creditCard;
    private String expiresMMYY;
    private String cc3;
    private String poNumber;
    private String taxId;
    private String duns;
    private String payPal;
    private String refId;
    @Index
    private Date createdOn = new Date();
    @Index
    private Date updatedOn;

    @Index
    public EnumContactType contactType;

    public ContactInfo() {
        this.createdOn = new Date();
    }

    public String getAddressLine1() {
        return this.addressLine1;
    }

    public String getAddressLine2() {
        return this.addressLine2;
    }

    public String getCc3() {
        return this.cc3;
    }

    public String getCity() {
        return this.city;
    }

    public String getCountry() {
        return this.country;
    }

    public Date getCreatedOn() {
        return this.createdOn;
    }

    public String getCreditCard() {
        return this.creditCard;
    }

    public String getDeviceID() {
        return this.deviceID;
    }

    public String getDuns() {
        return this.duns;
    }

    public String getEmail() {
        return email;
    }

    public String getExpiresMMYY() {
        return this.expiresMMYY;
    }

    public String getName() {
        return this.name;
    }

    public Long getParentKey() {
        return this.parentKey;
    }

    public String getPassword() {
        return password;
    }

    public String getPaymentType() {
        return this.paymentType;
    }

    public String getPayPal() {
        return this.payPal;
    }

    public String getPhoneNumber() {
        return this.phoneNumber;
    }

    public String getPoNumber() {
        return this.poNumber;
    }

    public String getPostalCode() {
        return this.postalCode;
    }

    public String getRefId() {
        return this.refId;
    }

    public String getState() {
        return this.state;
    }

    public String getTaxId() {
        return this.taxId;
    }

    public Date getUpdatedOn() {
        return this.updatedOn;
    }


    public void setAddressLine1(final String addressLine1) {
        this.addressLine1 = addressLine1;
    }

    public void setAddressLine2(final String addressLine2) {
        this.addressLine2 = addressLine2;
    }

    public void setCc3(final String cc3) {
        this.cc3 = cc3;
    }

    public void setCity(final String city) {
        this.city = city;
    }

    public void setCountry(final String country) {
        this.country = country;
    }

    public void setCreatedOn(final Date createdOn) {
        this.createdOn = createdOn;
    }

    public void setCreditCard(final String creditCard) {
        this.creditCard = creditCard;
    }

    public void setDeviceID(final String deviceID) {
        this.deviceID = deviceID;
    }

    public void setDuns(final String duns) {
        this.duns = duns;
    }

    public void setEmail(String email) {
        this.email = email;
    }

    public void setExpiresMMYY(final String expiresMMYY) {
        this.expiresMMYY = expiresMMYY;
    }

    public void setName(final String name) {
        this.name = name;
    }

    public void setParentKey(final Long parentKey) {
        this.parentKey = parentKey;
    }

    public void setPassword(String password) {
        this.password = password;
    }

    public void setPaymentType(final String paymentType) {
        this.paymentType = paymentType;
    }

    public void setPayPal(final String payPal) {
        this.payPal = payPal;
    }

    public void setPhoneNumber(final String phoneNumber) {
        this.phoneNumber = phoneNumber;
    }

    public void setPoNumber(final String poNumber) {
        this.poNumber = poNumber;
    }

    public void setPostalCode(final String postalCode) {
        this.postalCode = postalCode;
    }

    public void setRefId(final String refId) {
        this.refId = refId;
    }

    public void setState(final String state) {
        this.state = state;
    }

    public void setTaxId(final String taxId) {
        this.taxId = taxId;
    }

    public void setUpdatedOn(final Date updatedOn) {
        this.updatedOn = updatedOn;
    }

}
