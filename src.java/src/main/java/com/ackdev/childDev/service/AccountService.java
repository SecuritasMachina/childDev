package com.ackdev.childDev.service;

import java.util.logging.Logger;

import javax.servlet.http.HttpServletRequest;

import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.autoconfigure.SpringBootApplication;
import org.springframework.web.bind.annotation.CrossOrigin;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

import com.ackdev.childDev.dto.ResetPasswordModel;
import com.ackdev.common.dto.CommonResponse;
import com.ackdev.store.UserStore;
import com.common.security.beans.SecurityBean;
import com.common.security.dto.RegisterUserDTO;
import com.common.security.jdo.UserAccountV2;

import io.swagger.annotations.ApiOperation;
import io.swagger.annotations.Authorization;

@SpringBootApplication
@RestController
@CrossOrigin(origins = "*")
@RequestMapping("/rest/account")
public class AccountService {
	private static final Logger LOG = Logger.getLogger(AccountService.class.getName());
	@Autowired
	private UserStore requestUserStore;

	@GetMapping("/token/{logon}/{password}")
	public UserAccountV2 getToken(@PathVariable String logon, @PathVariable String password,
			HttpServletRequest request) {
		LOG.info("Logon Attenpt: " + logon);
		UserAccountV2 ret = SecurityBean.getInstance().doLogon(logon, password);
		return ret;
	}

	@GetMapping("/checkNameAvailable/{logon}")
	public CommonResponse.Status checkNameAvailable(@PathVariable String logon) {
		boolean ret1 = SecurityBean.getInstance().getCheckLogonAvailable(logon);
		if (ret1) {
			return new CommonResponse.Status().ok();
		}
		return new CommonResponse.Status().fail("Nickname unavailable");
	}

	@GetMapping(path = "/secure/getUser")
	public UserAccountV2 getUser() {
		LOG.info("requestUserStore:" + requestUserStore + " requestUserStore.getUserAccount(): "
				+ requestUserStore.getUserAccount());

		return requestUserStore.getUserAccount();
	}

	@PostMapping(path = "/register", consumes = "application/json", produces = "application/json")
	public UserAccountV2 register(@RequestBody RegisterUserDTO pRegisterUserDTO, HttpServletRequest request) {
		String remoteAddr = request.getRemoteAddr();
		LOG.info("requestUserStore: " + this.requestUserStore);
		UserAccountV2 ret = SecurityBean.getInstance().doRegisterUser(remoteAddr, pRegisterUserDTO);
		return ret;
	}

	@PostMapping(path = "/resetPW", consumes = "application/json", produces = "application/json")
	@ApiOperation(value = "Reset account password", notes = "Rate limited", response = String.class, authorizations = @Authorization(value = "api_key"))
	public String resetPW(@RequestBody ResetPasswordModel journalModel, HttpServletRequest request) {
		String ret = null;
		return ret;
	}
}
