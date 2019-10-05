package com.ackdev.jdo.security;

import java.util.Date;

import com.google.appengine.api.datastore.Text;
import com.googlecode.objectify.annotation.Cache;
import com.googlecode.objectify.annotation.Entity;
import com.googlecode.objectify.annotation.Id;
import com.googlecode.objectify.annotation.Index;
import com.googlecode.objectify.annotation.Unindex;

@Entity
@Unindex
@Cache
public class ContactsJDO  {
    static final long serialVersionUID = 1L;
    @Id
    public Long id;

    @Index
    private String addressLine1;

    @Index
    private String addressLine2;

    @Index
    private Date changedDate;

    @Index
    private String city;

    private Text comments;

    @Index
    private String companyName;

    @Index
    private String country;

    @Index
    private String emailAddress;

    @Index
    private String enteredBy;

    @Index
    private Date enteredDate;

    @Index
    private String firstName;


    @Index
    private String lastName;

    //@Index
    //private OrdersJDO ordersJDO;

    @Index
    private String phoneNbr;

    @Index
    private String postalCode;

    @Index
    private String state;

    @Index
    private String title;

    public ContactsJDO() {
        // TODO Auto-generated constructor stub
    }

    public String getAddressLine1() {
        return this.addressLine1;
    }

    public String getAddressLine2() {
        return this.addressLine2;
    }

    public Date getChangedDate() {
        return this.changedDate;
    }

    public String getCity() {
        return this.city;
    }

    public String getComments() {
        if (this.comments != null) {
            return this.comments.getValue();
        } else {
            return "";
        }
    }

    public String getCompanyName() {
        return this.companyName;
    }

    public String getCountry() {
        return this.country;
    }

    public String getEmailAddress() {
        return this.emailAddress;
    }

    public String getEnteredBy() {
        return this.enteredBy;
    }

    public Date getEnteredDate() {
        return this.enteredDate;
    }

    public String getFirstName() {
        return this.firstName;
    }


    public String getLastName() {
        return this.lastName;
    }


    public String getPhoneNbr() {
        return this.phoneNbr;
    }

    public String getPostalCode() {
        return this.postalCode;
    }

    public String getState() {
        return this.state;
    }

    public String getTitle() {
        return this.title;
    }

    public void setAddressLine1(final String addressLine1) {
        this.addressLine1 = addressLine1;
    }

    public void setAddressLine2(final String addressLine2) {
        this.addressLine2 = addressLine2;
    }

    public void setChangedDate(final Date changedDate) {
        this.changedDate = changedDate;
    }

    public void setCity(final String city) {
        this.city = city;
    }

    public void setComments(final String comments) {
        this.comments = new Text(comments);
    }

    public void setCompanyName(final String companyName) {
        this.companyName = companyName;
    }

    public void setCountry(final String country) {
        this.country = country;
    }

    public void setEmailAddress(final String emailAddress) {
        this.emailAddress = emailAddress;
    }

    public void setEnteredBy(final String enteredBy) {
        this.enteredBy = enteredBy;
    }

    public void setEnteredDate(final Date enteredDate) {
        this.enteredDate = enteredDate;
    }

    public void setFirstName(final String firstName) {
        this.firstName = firstName;
    }

    public void setLastName(final String lastName) {
        this.lastName = lastName;
    }


    public void setPhoneNbr(final String phoneNbr) {
        this.phoneNbr = phoneNbr;
    }

    public void setPostalCode(final String postalCode) {
        this.postalCode = postalCode;
    }

    public void setState(final String state) {
        this.state = state;
    }

    public void setTitle(final String title) {
        this.title = title;
    }

}
